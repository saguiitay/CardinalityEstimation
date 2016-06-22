# CardinalityEstimation
HyperLogLog-based set cardinality estimation library

This library estimates the number of unique elements in a set, in a quick and memory-efficient manner.  It's based on the following:

1. Flajolet et al., "HyperLogLog: the analysis of a near-optimal cardinality estimation algorithm", DMTCS proc. AH 2007, http://algo.inria.fr/flajolet/Publications/FlFuGaMe07.pdf
2. Heule, Nunkesser and Hall 2013, "HyperLogLog in Practice: Algorithmic Engineering of a State of The Art Cardinality Estimation Algorithm", http://static.googleusercontent.com/external_content/untrusted_dlcp/research.google.com/en/us/pubs/archive/40671.pdf

The accuracy/memory usage are user-selectable.  Typically, a cardinality estimator will give a perfect estimate of small cardinalities (up to 100 unique elements), and 97% accuracy or better (usually much better) for any cardinality up to near 2^64, while consuming several KB of memory (no more than 16KB).

## Usage
Usage is very simple:
```
ICardinalityEstimator<string> estimator = new CardinalityEstimator();

estimator.Add("Alice");
estimator.Add("Bob");
estimator.Add("Alice");
estimator.Add("George Michael");

ulong numberOfuniqueElements = estimator.Count(); // will be 3
```

## Nuget Package
This code is available as the Nuget package [`CardinalityEstimation`](https://www.nuget.org/packages/CardinalityEstimation/).  To install, run the following command in the Package Manager Console:
```
Install-Package CardinalityEstimation
```

### Keeping things friendly
This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
