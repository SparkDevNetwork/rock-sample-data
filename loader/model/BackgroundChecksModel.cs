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
    /// configuration/background-checks.json: who has a background check, how old it is, and the requirements that read it.
    /// </summary>
    internal sealed class BackgroundChecksConfiguration
    {
        /// <summary>
        /// Rock's Background Check Required requirement type.
        /// </summary>
        public Guid RequirementTypeGuid { get; set; }

        public BackgroundCheckRules Rules { get; set; }

        public LeaderRequirementDefinition LeaderRequirement { get; set; }

        public VolunteerCheckRule Volunteers { get; set; }

        public LeaderCheckRule Leaders { get; set; }

        /// <summary>
        /// Cast people with no background check, whatever their roles.
        /// </summary>
        public List<CastWithoutCheck> CastWithoutCheck { get; set; } = new List<CastWithoutCheck>();

        /// <summary>
        /// What Rock shows for each Cast background check story, checked after every load with background checks.
        /// </summary>
        public List<CastCheckExpectation> CastExpectations { get; set; } = new List<CastCheckExpectation>();

        public BackgroundCheckRequestRule Requests { get; set; }

        public CheckrDefinition Checkr { get; set; }
    }

    internal sealed class CastCheckExpectation
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The latest check: Pass, Fail, InProgress, or None.
        /// </summary>
        public string Check { get; set; }

        /// <summary>
        /// Whether the latest check is linked to the person's request to serve.
        /// </summary>
        public bool LinkedToRequest { get; set; }

        /// <summary>
        /// The small group leader requirement's result: Met, MetWithWarning, NotMet, or null when the story does not say.
        /// </summary>
        public string LeaderRequirement { get; set; }

        public string Story { get; set; }
    }

    /// <summary>
    /// Rock's own rules for the Background Check Required type, in days before install day.
    /// </summary>
    internal sealed class BackgroundCheckRules
    {
        /// <summary>
        /// A Pass meets the requirement while it is no older than this.
        /// </summary>
        public int ValidDays { get; set; }

        /// <summary>
        /// A Pass older than this, and still valid, meets the requirement with a warning.
        /// </summary>
        public int WarningAfterDays { get; set; }
    }

    internal sealed class LeaderRequirementDefinition
    {
        public Guid Guid { get; set; }

        /// <summary>
        /// Rock's Small Group type.
        /// </summary>
        public Guid GroupTypeGuid { get; set; }

        /// <summary>
        /// The role the requirement applies to, by name.
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// How many years ago the church added the requirement.
        /// </summary>
        public int AddedYearsAgo { get; set; }
    }

    internal sealed class VolunteerCheckRule
    {
        /// <summary>
        /// The range a volunteer's passing check is drawn from, in days before install day.
        /// </summary>
        public int[] CheckedDaysAgo { get; set; }
    }

    internal sealed class LeaderCheckRule
    {
        /// <summary>
        /// The share of generated leaders in each state, by state name.
        /// </summary>
        public Dictionary<string, double> Mix { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The range each state's check is drawn from, in days before install day.
        /// </summary>
        public Dictionary<string, int[]> DaysAgo { get; set; } = new Dictionary<string, int[]>();
    }

    internal sealed class CastWithoutCheck
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        public string Story { get; set; }
    }

    internal sealed class BackgroundCheckRequestRule
    {
        /// <summary>
        /// The Background Check connection status in configuration/connections.json.
        /// </summary>
        public Guid StatusGuid { get; set; }

        /// <summary>
        /// How many days after the request reached the status its check was requested.
        /// </summary>
        public int[] RequestedDaysAfterStatus { get; set; }
    }

    internal sealed class CheckrDefinition
    {
        /// <summary>
        /// The package name Checkr's webhook stores on the check.
        /// </summary>
        public string PackageName { get; set; }

        /// <summary>
        /// The package's name on the report.
        /// </summary>
        public string PackageTitle { get; set; }

        /// <summary>
        /// How many days before the result the check was requested.
        /// </summary>
        public int[] RequestedDaysBeforeResult { get; set; }

        /// <summary>
        /// The searches the package runs, in the order the report lists them.
        /// </summary>
        public List<string> Searches { get; set; } = new List<string>();
    }
}
