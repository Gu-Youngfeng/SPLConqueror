﻿using MachineLearning.Sampling.Heuristics;
using MachineLearning.Sampling.Hybrid.Distributive;
using MachineLearning.Solver;
using SPLConqueror_Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MachineLearning.Sampling.Hybrid.Distributive
{
    /// <summary>
    /// This class summarizes the methods of distribution-aware and distribution-preserving sampling and is realized by using
    /// the strategy pattern. 
    /// In these sampling strategies, the configurations are divided in buckets and the sampled configurations are selected 
    /// from this buckets according to a given distribution (e.g., uniform, normal distribution).
    /// </summary>
    public abstract class DistributionSensitive : HybridStrategy
    {
        #region variables
        #region constants
        public const string NUM_CONFIGS = "numConfigs";
        public const string DISTRIBUTION = "distribution";
        public const string DISTANCE_METRIC = "distance-metric";
        public const string AS_TW = "asTW";
        public const string ONLY_NUMERIC = "onlyNumeric";
        public const string ONLY_BINARY = "onlyBinary";
        public const string SEED = "seed";
        public const int ROUND_FACTOR = 4;
        public static DistanceMetric[] metrics = { new ManhattanDistance() };
        public static Distribution[] distributions = { new UniformDistribution() };
        #endregion

        protected DistanceMetric metric = null;
        protected Distribution distribution = null;
        #endregion

        /// <summary>
        /// The constructor initializes the parameters needed for this class and its default values.
        /// These may be overwritten by <see cref="HybridStrategy.SetSamplingParameters(Dictionary{string, string})"/>.
        /// </summary>
        public DistributionSensitive() : base()
        {
            this.strategyParameter = new Dictionary<string, string>()
            {
                {DISTANCE_METRIC, "manhattan" },
                {DISTRIBUTION, "uniform" },
                {NUM_CONFIGS, "asTW2" },
                {ONLY_NUMERIC, "false" },
                {ONLY_BINARY, "false" },
                {SEED, "0" }
            };
        }

        /// <summary>
        /// Computes a distribution-aware sample set.
        /// </summary>
        /// <returns><code>True</code> iff the process was successfull;<code>False</code> otherwise</returns>
        public override bool ComputeSamplingStrategy()
        {
            // Check configuration and set the according variables
            CheckConfiguration();

            // Before beginning with the computation, the number of configurations of another sampling strategy is computed if no exact number is provided
            int numberConfigs;
            if (!int.TryParse(this.strategyParameter[NUM_CONFIGS], out numberConfigs))
            {
                numberConfigs = CountConfigurations();
            }

            // Now, compute all buckets according to the given features
            List<double> allBuckets = ComputeBuckets();

            // Compute the whole population needed for sampling from the buckets
            Dictionary<double, List<Configuration>> wholeDistribution = ComputeDistribution(allBuckets);

            // Then, sample from all buckets according to the given distribution
            SampleFromDistribution(wholeDistribution, allBuckets, numberConfigs);

            return true;
        }

        /// <summary>
        /// This method checks the configuration and sets the according variables.
        /// </summary>
        private void CheckConfiguration()
        {
            // Check the used metric
            string metricToUse = this.strategyParameter[DISTANCE_METRIC];
            foreach (DistanceMetric m in DistributionSensitive.metrics)
            {
                if (m.GetName().ToUpper().Equals(metricToUse.ToUpper()))
                {
                    this.metric = m;
                    break;
                }
            }

            if (this.metric == null)
            {
                throw new ArgumentException("The metric " + metricToUse + " is not supported.");
            }

            // Check the used distribution
            string distributionToUse = this.strategyParameter[DISTRIBUTION];
            foreach (Distribution d in DistributionSensitive.distributions)
            {
                if (d.GetName().ToUpper().Equals(distributionToUse.ToUpper()))
                {
                    this.distribution = d;
                    break;
                }
            }

            if (this.distribution == null)
            {
                throw new ArgumentException("The metric " + distributionToUse + " is not supported.");
            }

            string onlyNumeric = this.strategyParameter[ONLY_NUMERIC];
            if (onlyNumeric.ToLower().Equals("true"))
            {
                this.optionsToConsider = new List<ConfigurationOption>();
                this.optionsToConsider.AddRange(GlobalState.varModel.NumericOptions);
            }

            string onlyBinary = this.strategyParameter[ONLY_BINARY];
            if (onlyBinary.ToLower().Equals("true"))
            {
                this.optionsToConsider = new List<ConfigurationOption>();
                this.optionsToConsider.AddRange(GlobalState.varModel.BinaryOptions);
            }

            // Both can't be set to true at the same time
            if (onlyBinary.ToLower().Equals("true") && onlyNumeric.ToLower().Equals("true"))
            {
                throw new ArgumentException("The options " + ONLY_BINARY + " and " + ONLY_NUMERIC + " can not be active at the same time.");
            }

            // If no options are given, select all options
            if (this.optionsToConsider == null)
            {
                this.optionsToConsider = new List<ConfigurationOption>();
                this.optionsToConsider.AddRange(GlobalState.varModel.BinaryOptions);
                this.optionsToConsider.AddRange(GlobalState.varModel.NumericOptions);
            }

        }

        /// <summary>
        /// Creates the wanted distribution.
        /// This method has to be implemented by the different sampling strategies (e.g., distribution-aware, distribution-preserving).
        /// </summary>
        /// <param name="wholeDistribution">a <see cref="Dictionary{TKey, TValue}"/> containing all configurations and their corresponding distance</param>
        /// <param name="allBuckets">all buckets of the distribution</param>
        public abstract Dictionary<double, double> CreateDistribution(Dictionary<double, List<Configuration>> wholeDistribution, List<double> allBuckets);

        /// <summary>
        /// Selects configurations of the given distribution by using the specified distribution (e.g., uniform).
        /// </summary>
        /// <param name="wholeDistribution">the distribution of all configurations</param>
        /// <param name="allBuckets">all buckets of the distribution</param>
        /// <param name="count">the number of configurations to sample</param>
        public void SampleFromDistribution(Dictionary<double, List<Configuration>> wholeDistribution, List<double> allBuckets, int count)
        {
            Dictionary<double, double> wantedDistribution = CreateDistribution(wholeDistribution, allBuckets);
            int seed = 0;
            Int32.TryParse(strategyParameter[SEED], out seed);
            Random rand = new Random(seed);

            while (this.selectedConfigurations.Count < count && HasSamples(wholeDistribution))
            {
                double randomDouble = rand.NextDouble();
                double currentProbability = 0;
                int currentBucket = 0;

                while (randomDouble > currentProbability + wantedDistribution.ElementAt(currentBucket).Value)
                {
                    currentBucket++;
                    currentProbability += wantedDistribution.ElementAt(currentBucket).Value;
                }

                double distanceOfBucket = wantedDistribution.ElementAt(currentBucket).Key;

                // If a bucket was selected that contains no more configurations, repeat the procedure
                if (wholeDistribution[distanceOfBucket].Count == 0)
                {
                    continue;
                }

                int numberConfiguration = rand.Next(0, wholeDistribution[distanceOfBucket].Count);

                this.selectedConfigurations.Add(wholeDistribution[distanceOfBucket][numberConfiguration]);
                wholeDistribution[distanceOfBucket].RemoveAt(numberConfiguration);

            }

            if (this.selectedConfigurations.Count < count)
            {
                GlobalState.logError.logLine("Sampled only " + this.selectedConfigurations.Count + " configurations as there are no more configurations.");
            }
        }

        /// <summary>
        /// Returns whether there are any more samples or not.
        /// </summary>
        /// <param name="wholeDistribution">the distribution of the configurations</param>
        /// <returns><code>True</code> iff there are any configurations left</returns>
        protected bool HasSamples(Dictionary<double, List<Configuration>> wholeDistribution)
        {
            foreach (double d in wholeDistribution.Keys)
            {
                if (wholeDistribution[d].Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// This method computes the distribution of the whole population using the <see cref="HybridStrategy.optionsToConsider"/>.
        /// </summary>
        /// <param name="allBuckets">all buckets of the distribution</param>
        /// <returns>a <see cref="Dictionary{TKey, TValue}"/> containing the bucket as key and a <see cref="List"/> of different configurations in these buckets</returns>
        private Dictionary<double, List<Configuration>> ComputeDistribution(List<double> allBuckets)
        {
            Dictionary<double, List<Configuration>> result = new Dictionary<double, List<Configuration>>();

            // Fill the dictionary
            foreach (double d in allBuckets)
            {
                result[d] = new List<Configuration>();
            }

            List<Configuration> allConfigurations = VariantGenerator.GenerateAllVariants(GlobalState.varModel, this.optionsToConsider);

            foreach (Configuration c in allConfigurations)
            {
                double distance = Math.Round(this.metric.ComputeDistance(c), ROUND_FACTOR);
                result[distance].Add(c);
            }

            // Remove empty buckets
            List<double> toRemove = new List<double>();
            foreach (double d in result.Keys)
            {
                if (result[d].Count == 0)
                {
                    toRemove.Add(d);
                }
            }
            foreach (double d in toRemove)
            {
                allBuckets.Remove(d);
                result.Remove(d);
            }

            return result;
        }

        /// <summary>
        /// Returns all possible buckets using the <see cref="HybridStrategy.optionsToConsider"/>.
        /// </summary>
        /// <returns>a <see cref="List"/> containing the sum of all value combinations of the features</returns>
        private List<double> ComputeBuckets()
        {
            List<List<double>> allValueSets = new List<List<double>>();
            foreach (ConfigurationOption o in this.optionsToConsider)
            {
                if (o is NumericOption)
                {
                    NumericOption numOpt = (NumericOption)o;
                    List<double> distances = new List<double>();
                    List<double> valuesOfNumericOption = numOpt.getAllValues();

                    foreach (double numOptValue in valuesOfNumericOption)
                    {
                        distances.Add(this.metric.ComputeDistanceOfNumericFeature(numOptValue, numOpt.Min_value, numOpt.Max_value));
                    }
                    allValueSets.Add(distances);
                }
                else
                {
                    BinaryOption binOpt = (BinaryOption)o;
                    if (!binOpt.Optional && CountChildren(binOpt, GlobalState.varModel) > 0)
                    {
                        allValueSets.Add(new List<double> { this.metric.ComputeDistanceOfBinaryFeature(1) });
                    }
                    else
                    {
                        allValueSets.Add(new List<double> { this.metric.ComputeDistanceOfBinaryFeature(0), this.metric.ComputeDistanceOfBinaryFeature(1) });
                    }
                }
            }

            List<double> result = ComputeSumOfCartesianProduct(allValueSets);

            // Sort the list
            result.Sort(delegate (double x, double y)
            {
                return x.CompareTo(y);
            });

            return result;
        }

        /// <summary>
        /// Returns the number of all children of the given feature.
        /// </summary>
        /// <param name="o">the configuration option to search children for</param>
        /// <param name="var">the variability model</param>
        /// <returns>the number of all children of the given feature</returns>
        private int CountChildren(ConfigurationOption o, VariabilityModel var)
        {
            int count = 0;
            foreach (ConfigurationOption conf in var.getOptions())
            {
                if (conf == o || conf.Parent == null)
                {
                    continue;
                }

                if (conf.Parent.Equals(o))
                {
                    count++;
                }
            }
            return count;
        }


        /// <summary>
        /// Computes the sum of the cartesion product of the given lists.
        /// Note that every bucket is only included once.
        /// </summary>
        /// <param name="remaining">the remaining lists</param>
        /// <returns>the sum of the different combinations</returns>
        private List<double> ComputeSumOfCartesianProduct(List<List<double>> remaining)
        {
            if (remaining.Count == 0)
            {
                return new List<double> { 0 };
            }

            List<double> currentList = remaining[0];

            remaining.RemoveAt(0);

            if (remaining.Count == 0)
            {
                return currentList;
            }
            else
            {
                List<double> sumOfRest = ComputeSumOfCartesianProduct(remaining);
                List<double> newList = new List<double>();
                foreach (double value in sumOfRest)
                {
                    foreach (double ownValue in currentList)
                    {
                        double newSum = Math.Round(value + ownValue, ROUND_FACTOR);
                        // Reduce the size of the new list by ignoring values that are already included
                        if (!newList.Contains(newSum))
                        {
                            newList.Add(newSum);
                        }
                    }
                }

                return newList;
            }
        }

        /// <summary>
        /// Counts the configurations from another sampling strategy.
        /// </summary>
        /// <returns>the number of configurations from the other sampling strategy</returns>
        private int CountConfigurations()
        {
            int numberConfigs;

            string numConfigsValue = this.strategyParameter[NUM_CONFIGS];
            // Only "asTW" is currently supported
            if (numConfigsValue.Contains(AS_TW))
            {
                numConfigsValue = numConfigsValue.Replace(AS_TW, "").Trim();
                int t;
                int.TryParse(numConfigsValue, out t);
                TWise tw = new TWise();
                numberConfigs = tw.generateT_WiseVariants_new(GlobalState.varModel, t).Count;
            }
            else
            {
                throw new ArgumentException("Only asTW is currently supported.");
            }

            return numberConfigs;
        }
    }
}