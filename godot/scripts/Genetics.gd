# Genetics module - simple Mendelian single-locus model with mutation
# Provides deterministic inheritance functions useful for tests

extends Node
class_name Genetics

# Example representation: genetics = {"growth": ["A","a"], "color": ["B","b"]}

func mate(parent_a: Dictionary, parent_b: Dictionary, rng: RandomNumberGenerator = null, mutation_rate: float = 0.0) -> Dictionary:
    # parent_* are genetics dictionaries
    var child = {}
    if rng == null:
        rng = RandomNumberGenerator.new()
        rng.randomize()
    for locus in parent_a.keys():
        var a_alleles = parent_a[locus]
        var b_alleles = parent_b.get(locus, a_alleles)
        # choose one from each parent
        var allele1 = a_alleles[rng.randi_range(0, a_alleles.size() - 1)]
        var allele2 = b_alleles[rng.randi_range(0, b_alleles.size() - 1)]
        # mutation
        allele1 = _maybe_mutate(allele1, rng, mutation_rate)
        allele2 = _maybe_mutate(allele2, rng, mutation_rate)
        child[locus] = [allele1, allele2]
    return child

func _maybe_mutate(allele: String, rng: RandomNumberGenerator, mutation_rate: float) -> String:
    if mutation_rate <= 0.0:
        return allele
    var p = rng.randf()
    if p < mutation_rate:
        # simple mutation: flip case if allele is single char, otherwise append "m"
        if allele.length() == 1:
            var ch = allele[0]
            if ch.is_upper():
                return allele.to_lower()
            else:
                return allele.to_upper()
        return allele + "m"
    return allele

func phenotype(genetics: Dictionary) -> Dictionary:
    var out = {}
    # example: growth locus 'G' additive model: count uppercase alleles
    for locus in genetics.keys():
        var alleles = genetics[locus]
        var score = 0
        for a in alleles:
            if a.length() > 0 and a[0].is_upper():
                score += 1
        out[locus] = score
    return out
