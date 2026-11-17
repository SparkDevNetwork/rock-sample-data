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
    /// configuration/assessments.json: who takes Rock's five assessments, who asks them to, and when (docs/assessments.md).
    /// </summary>
    internal sealed class AssessmentsConfiguration
    {
        /// <summary>
        /// The day the church began using Rock's assessments. Nothing is requested before it.
        /// </summary>
        public RelativeDate Adopted { get; set; }

        /// <summary>
        /// Rock's five assessment types, by key.
        /// </summary>
        public List<AssessmentTypeDefinition> Types { get; set; } = new List<AssessmentTypeDefinition>();

        public StaffAssessmentRule Staff { get; set; }

        public List<ClassAssessmentRule> Classes { get; set; } = new List<ClassAssessmentRule>();

        public LeaderAssessmentRule Leaders { get; set; }

        public SelfStartedAssessmentRule SelfStarted { get; set; }

        /// <summary>
        /// Extra leanings by role ("small-group-leader" or "team-leader"): by assessment type key, each dimension's lean.
        /// </summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, double>>> Leanings { get; set; } = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>();
    }

    internal sealed class AssessmentTypeDefinition
    {
        /// <summary>
        /// disc, spiritual-gifts, conflict-profile, emotional-intelligence, or motivators.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Rock's AssessmentType Guid.
        /// </summary>
        public Guid Guid { get; set; }

        /// <summary>
        /// How long the assessment takes, in minutes.
        /// </summary>
        public IntRange Minutes { get; set; } = new IntRange();
    }

    /// <summary>
    /// Every staff member is asked to take these when they start, or when the church adopted assessments.
    /// </summary>
    internal sealed class StaffAssessmentRule
    {
        public List<string> Types { get; set; } = new List<string>();

        /// <summary>
        /// "supervisor": each staff member's supervisor asks. A staff member with no supervisor takes them on their own.
        /// </summary>
        public string RequestedBy { get; set; }

        public int DueInDays { get; set; }

        public int FinishWithinDays { get; set; }

        /// <summary>
        /// Staff who started this recently have not taken them yet, and their requests stay pending.
        /// </summary>
        public int PendingIfStartedWithinDays { get; set; }
    }

    /// <summary>
    /// A class asks everyone in it to take these: one request per person per class, on a set night.
    /// </summary>
    internal sealed class ClassAssessmentRule
    {
        /// <summary>
        /// The class's step type key in configuration/steps.json.
        /// </summary>
        public string StepType { get; set; }

        public List<string> Types { get; set; } = new List<string>();

        /// <summary>
        /// The class night the request goes out, counting the first night as 1.
        /// </summary>
        public int RequestOnNight { get; set; }

        /// <summary>
        /// The class night it is due.
        /// </summary>
        public int DueOnNight { get; set; }

        /// <summary>
        /// "campus-pastor" or a staff key.
        /// </summary>
        public string RequestedBy { get; set; }

        /// <summary>
        /// The share of people who finish, each within <see cref="FinishWithinDays"/>. The rest stay pending.
        /// </summary>
        public double PercentFinished { get; set; }

        public int FinishWithinDays { get; set; }
    }

    /// <summary>
    /// Every current small group and serving team leader is asked to take these before they start leading.
    /// </summary>
    internal sealed class LeaderAssessmentRule
    {
        public List<string> Types { get; set; } = new List<string>();

        /// <summary>
        /// Who asks, by group kind ("small-group" or "serving"): "campus-pastor" or a staff key.
        /// </summary>
        public Dictionary<string, string> RequestedBy { get; set; } = new Dictionary<string, string>();

        public IntRange RequestDaysBeforeLeading { get; set; } = new IntRange();

        public int DueInDays { get; set; }

        public int FinishWithinDays { get; set; }
    }

    /// <summary>
    /// Some active adults take one of these on their own, with no request.
    /// </summary>
    internal sealed class SelfStartedAssessmentRule
    {
        public List<string> Types { get; set; } = new List<string>();

        public double PercentOfActiveAdultsPerYear { get; set; }
    }

    /// <summary>
    /// One Cast person's assessment story for one type (<c>assessments</c> in the household file).
    /// </summary>
    internal sealed class CastAssessment
    {
        /// <summary>
        /// The dimensions the person's answers lean toward, which must come out on top, such as the gifts that fit a greeter.
        /// </summary>
        public List<string> Leaning { get; set; } = new List<string>();

        /// <summary>
        /// "complete" or "pending": how the person answers every request the rules make of this type. Null leaves it to the rules' shares.
        /// </summary>
        public string Requests { get; set; }

        /// <summary>
        /// The person's own history of this type, which replaces the rules for it.
        /// </summary>
        public List<CastAssessmentTake> Takes { get; set; } = new List<CastAssessmentTake>();
    }

    /// <summary>
    /// One Cast request or result. A request with no completion stays pending. A completion with no request was taken on
    /// the person's own.
    /// </summary>
    internal sealed class CastAssessmentTake
    {
        public RelativeDate Requested { get; set; }

        /// <summary>
        /// The staff key of who asked.
        /// </summary>
        public string RequestedBy { get; set; }

        public RelativeDate Completed { get; set; }

        /// <summary>
        /// DISC scores kept from before the church used Rock's assessments, with no answers behind them.
        /// </summary>
        public CastDiscScores Legacy { get; set; }
    }

    /// <summary>
    /// A legacy DISC result, exactly as Rock shows it.
    /// </summary>
    internal sealed class CastDiscScores
    {
        public Dictionary<string, int> Adaptive { get; set; } = new Dictionary<string, int>();

        public Dictionary<string, int> Natural { get; set; } = new Dictionary<string, int>();

        public string PersonalityType { get; set; }
    }

    /// <summary>
    /// What Rock should show of a Cast person's assessments after a load.
    /// </summary>
    internal sealed class CastAssessmentsExpect
    {
        /// <summary>
        /// Types with a completed result.
        /// </summary>
        public List<string> Complete { get; set; } = new List<string>();

        /// <summary>
        /// Types whose latest request is still pending.
        /// </summary>
        public List<string> Pending { get; set; } = new List<string>();
    }
}
