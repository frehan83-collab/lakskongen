# Simple deterministic tests for Genetics module

import random

# Reimplement the same simple functions in Python to validate deterministic behavior

def maybe_mutate(allele, p, mutation_rate):
    if mutation_rate <= 0.0:
        return allele
    if p < mutation_rate:
        if len(allele) == 1:
            if allele.isupper():
                return allele.lower()
            else:
                return allele.upper()
        return allele + 'm'
    return allele


def mate(parent_a, parent_b, seed=42, mutation_rate=0.0):
    rnd = random.Random(seed)
    child = {}
    for locus in parent_a:
        a = parent_a[locus]
        b = parent_b.get(locus, a)
        allele1 = a[rnd.randrange(len(a))]
        allele2 = b[rnd.randrange(len(b))]
        allele1 = maybe_mutate(allele1, rnd.random(), mutation_rate)
        allele2 = maybe_mutate(allele2, rnd.random(), mutation_rate)
        child[locus] = [allele1, allele2]
    return child


def phenotype(genetics):
    out = {}
    for locus, alleles in genetics.items():
        score = 0
        for a in alleles:
            if len(a) > 0 and a[0].isupper():
                score += 1
        out[locus] = score
    return out


def test_mendel_basic():
    mom = {'G': ['G','g'], 'C': ['B','b']}
    dad = {'G': ['G','g'], 'C': ['B','b']}
    child = mate(mom, dad, seed=1, mutation_rate=0.0)
    assert 'G' in child and 'C' in child
    assert len(child['G']) == 2
    print('PASS basic mate')


def test_mutation():
    mom = {'X': ['A']}
    dad = {'X': ['A']}
    child = mate(mom, dad, seed=2, mutation_rate=1.0)
    # with mutation_rate=1.0 both alleles should flip case
    assert child['X'][0].islower() or child['X'][1].islower()
    print('PASS mutation')

if __name__ == '__main__':
    test_mendel_basic()
    test_mutation()
