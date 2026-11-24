// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// The contents of configuration/rock-metrics.json: history for the metrics Rock ships (issue 55).
    /// </summary>
    internal sealed class RockMetricsConfiguration
    {
        /// <summary>
        /// The weekly and Tithing Overview metrics, each counted by its own rule in the loader.
        /// </summary>
        public List<RockMetricHistory> Metrics { get; set; } = new List<RockMetricHistory>();

        public InsightsHistory Insights { get; set; }

        public ServiceEntrySettings ServiceEntry { get; set; }
    }

    /// <summary>
    /// One Rock metric and how far back its values go.
    /// </summary>
    internal sealed class RockMetricHistory
    {
        /// <summary>
        /// Names the loader's rule for the metric, such as "prayers".
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Rock's title for it, for people reading the file.
        /// </summary>
        public string Title { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// How many weeks of values to write, up to install.
        /// </summary>
        public int Weeks { get; set; }

        /// <summary>
        /// The church's SQL for the metric, one string per line, in place of Rock's, or null to keep Rock's.
        /// </summary>
        public List<string> SourceSql { get; set; }
    }

    /// <summary>
    /// Rock's Insights metrics: every SQL metric in the categories, run as of each monthly run.
    /// </summary>
    internal sealed class InsightsHistory
    {
        public int Months { get; set; }

        public List<Guid> Categories { get; set; } = new List<Guid>();
    }

    /// <summary>
    /// The two weekly metrics staff type into Rock's Service Metrics Entry block.
    /// </summary>
    internal sealed class ServiceEntrySettings
    {
        /// <summary>
        /// When staff finish typing the weekend's numbers on Sunday, such as "13:30".
        /// </summary>
        public string EnteredAt { get; set; }

        /// <summary>
        /// The key in steps.json of the step that records a decision to follow Jesus.
        /// </summary>
        public string DecisionStepType { get; set; }

        /// <summary>
        /// The key in steps.json of the Baptism step.
        /// </summary>
        public string BaptismStepType { get; set; }

        public int MinimumDaysBeforeBaptism { get; set; }

        public int MaximumDaysBeforeBaptism { get; set; }
    }
}
