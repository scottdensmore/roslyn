﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Internal.Log
{
    internal struct StatisticResult
    {
        public static StatisticResult FromList(List<int> values)
        {
            if (values.Count == 0)
            {
                return default;
            }

            var max = int.MinValue;
            var min = int.MaxValue;

            var total = 0;
            for (var i = 0; i < values.Count; i++)
            {
                var current = values[i];
                max = max < current ? current : max;
                min = min > current ? current : min;

                total += current;
            }

            var mean = total / values.Count;
            var median = values[values.Count / 2];

            var range = max - min;
            var mode = values.GroupBy(i => i).OrderByDescending(g => g.Count()).FirstOrDefault().Key;

            return new StatisticResult(max, min, median, mean, range, mode, values.Count);
        }

        /// <summary>
        /// maximum value
        /// </summary>
        public readonly int Maximum;

        /// <summary>
        /// minimum value
        /// </summary>
        public readonly int Minimum;

        /// <summary>
        /// middle value of the total data set
        /// </summary>
        public readonly int? Median;

        /// <summary>
        /// average value of the total data set
        /// </summary>
        public readonly int Mean;

        /// <summary>
        /// most frequent value in the total data set
        /// </summary>
        public readonly int? Mode;

        /// <summary>
        /// difference between max and min value
        /// </summary>
        public readonly int Range;

        /// <summary>
        /// number of data points in the total data set
        /// </summary>
        public readonly int Count;

        public StatisticResult(int max, int min, int? median, int mean, int range, int? mode, int count)
        {
            this.Maximum = max;
            this.Minimum = min;
            this.Median = median;
            this.Mean = mean;
            this.Range = range;
            this.Mode = mode;
            this.Count = count;
        }

        /// <summary>
        /// Writes out these statistics to a property bag for sending to telemetry.
        /// </summary>
        /// <param name="prefix">The prefix given to any properties written; if you want a period as a separator, it's
        /// your responsibility to pass that.</param>
        public void WriteTelemetryPropertiesTo(Dictionary<string, object?> properties, string prefix = "")
        {
            properties.Add(prefix + nameof(Maximum), Maximum);
            properties.Add(prefix + nameof(Minimum), Minimum);
            properties.Add(prefix + nameof(Mean), Mean);
            properties.Add(prefix + nameof(Range), Range);
            properties.Add(prefix + nameof(Count), Count);

            if (Median.HasValue)
                properties.Add(prefix + nameof(Median), Median.Value);

            if (Mode.HasValue)
                properties.Add(prefix + nameof(Mode), Mode.Value);
        }
    }
}
