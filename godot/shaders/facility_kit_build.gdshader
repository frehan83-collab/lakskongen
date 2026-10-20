shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
// The facility kit while it is being built (F2-20, LK_FacilityKitBuild): the
// body of facility_kit.gdshader drawn double-sided, so a tank whose walls
// are still rising (INSTANCE_CUSTOM.r) or a hall going up (build_cut_m)
// shows the inside of its far wall instead of a gap. The presenter swaps a
// shell to it only while it stands cut open; everything finished stays on
// the back-face-culled kit.
#define LK_FACILITY_BUILD
#include "res://shaders/include/facility_kit_body.gdshaderinc"
