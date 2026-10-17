class_name AudioStreamCache
extends RefCounted

## The AudioController's streams by path (wave 3 ui-a). Every file is read
## once and kept, and a track (music, ambience, a weather layer) is read on
## a worker thread, so a day of the sim never waits on the disk: at 30x a
## season's music or a weather layer read on the main thread inside the
## tick cost 9-11 ms and a UI cue up to 9 ms (measured headless, po3_red_sone
## seed 91, load ~8-13).

var _streams: Dictionary = {}   # path -> AudioStream
var _exists: Dictionary = {}    # path -> bool
var _waiting: Dictionary = {}   # path -> Array[Callable] run with the stream


## Whether the file is there (asked of the disk once per path).
func exists(path: String) -> bool:
	if path.is_empty():
		return false
	if not _exists.has(path):
		_exists[path] = ResourceLoader.exists(path)
	return _exists[path]


## The stream now: from the cache, else read here (a short cue whose
## background read has not finished waits for it).
func now(path: String) -> AudioStream:
	if _streams.has(path):
		return _streams[path]
	if not exists(path):
		return null
	if _waiting.has(path):
		_deliver(path, ResourceLoader.load_threaded_get(path) as AudioStream)
		return _streams.get(path)
	var stream := load(path) as AudioStream
	if stream != null:
		_streams[path] = stream
	return stream


## Call `ready_fn(stream)` with the stream: at once when it is cached,
## otherwise from poll() once a worker thread has read it. False when the
## file is missing (ready_fn is never called).
func later(path: String, ready_fn: Callable) -> bool:
	if _streams.has(path):
		ready_fn.call(_streams[path])
		return true
	if not exists(path):
		return false
	if not _waiting.has(path):
		if ResourceLoader.load_threaded_request(path, "AudioStream") != OK:
			var stream := now(path)
			if stream != null:
				ready_fn.call(stream)
			return stream != null
		_waiting[path] = []
	_waiting[path].append(ready_fn)
	return true


## Start reading these files in the background (the UI cues at launch).
func prefetch(paths: Array) -> void:
	for path in paths:
		var p := str(path)
		if exists(p) and not _streams.has(p) and not _waiting.has(p):
			if ResourceLoader.load_threaded_request(p, "AudioStream") == OK:
				_waiting[p] = []


## Hand finished reads to their callers; once a frame (cheap when idle).
func poll() -> void:
	if _waiting.is_empty():
		return
	for path in _waiting.keys():
		var status := ResourceLoader.load_threaded_get_status(path)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			continue
		var stream: AudioStream = null
		if status == ResourceLoader.THREAD_LOAD_LOADED:
			stream = ResourceLoader.load_threaded_get(path) as AudioStream
		_deliver(path, stream)


## True while a read is in flight.
func is_loading(path: String = "") -> bool:
	return not _waiting.is_empty() if path.is_empty() else _waiting.has(path)


func _deliver(path: String, stream: AudioStream) -> void:
	var fns: Array = _waiting.get(path, [])
	_waiting.erase(path)
	if stream == null:
		push_warning("AudioController: failed to load " + path)
		return
	_streams[path] = stream
	for fn: Callable in fns:
		if fn.is_valid():
			fn.call(stream)
