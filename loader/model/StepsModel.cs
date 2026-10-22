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
    /*
        These classes mirror configuration/steps.json. Property names match the JSON keys (case-insensitive).
    */

    /// <summary>
    /// The contents of configuration/steps.json: the adult step program, its statuses, and its step types,
    /// each with the record its steps come from.
    /// </summary>
    internal sealed class StepsConfiguration
    {
        public StepProgramDefinition Program { get; set; }

        public StepStatusGuids Statuses { get; set; }

        /// <summary>
        /// Rock's Organizational Objective defined value Guids by the key the step types use.
        /// </summary>
        public Dictionary<string, Guid> Objectives { get; set; } = new Dictionary<string, Guid>();

        public List<StepPrerequisiteDefinition> Prerequisites { get; set; } = new List<StepPrerequisiteDefinition>();

        /// <summary>
        /// The step types in path order.
        /// </summary>
        public List<StepTypeDefinition> StepTypes { get; set; } = new List<StepTypeDefinition>();
    }

    internal sealed class StepProgramDefinition
    {
        /// <summary>
        /// Rock's Discipleship Path program.
        /// </summary>
        public Guid Guid { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// One of Rock's CompletionFlow names, such as LinearPreferred.
        /// </summary>
        public string CompletionFlow { get; set; }
    }

    internal sealed class StepStatusGuids
    {
        public Guid InProgress { get; set; }

        public Guid Complete { get; set; }
    }

    internal sealed class StepPrerequisiteDefinition
    {
        /// <summary>
        /// The step type key that needs the prerequisite.
        /// </summary>
        public string StepType { get; set; }

        /// <summary>
        /// The step type key that must be complete first.
        /// </summary>
        public string Requires { get; set; }
    }

    /// <summary>
    /// One step type: one of Rock's, updated in place, or a new one.
    /// </summary>
    internal sealed class StepTypeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// True for a step type Rock ships, which is kept and updated in place.
        /// </summary>
        public bool RockType { get; set; }

        /// <summary>
        /// The Ministry by Design stage. Documentation only; Rock has no such field.
        /// </summary>
        public string Stage { get; set; }

        /// <summary>
        /// Milestone or Rhythm, Rock's EngagementType names.
        /// </summary>
        public string Engagement { get; set; }

        /// <summary>
        /// The key of the type's organizational objective in <see cref="StepsConfiguration.Objectives"/>.
        /// </summary>
        public string Objective { get; set; }

        public int Impact { get; set; }

        public string IconCssClass { get; set; }

        public string HighlightColor { get; set; }

        public bool AllowMultiple { get; set; }

        public bool HasEndDate { get; set; }

        /// <summary>
        /// The description, or null to keep Rock's on a Rock type.
        /// </summary>
        public string Description { get; set; }

        public string CallToActionLabel { get; set; }

        public string CallToActionDescription { get; set; }

        public StepSource Source { get; set; }
    }

    /// <summary>
    /// The record a step type's steps come from (decision 86). <see cref="Kind"/> says which, and only that
    /// kind's settings are filled.
    /// </summary>
    internal sealed class StepSource
    {
        /// <summary>
        /// class, membership, baptism, or giving-journey.
        /// </summary>
        public string Kind { get; set; }

        #region membership

        /// <summary>
        /// small-group, serving, or both.
        /// </summary>
        public List<string> GroupTypes { get; set; } = new List<string>();

        /// <summary>
        /// True when only memberships in a leader role count.
        /// </summary>
        public bool LeadersOnly { get; set; }

        /// <summary>
        /// True when a pending membership is an In Progress step. Otherwise a pending membership is no step.
        /// </summary>
        public bool PendingIsInProgress { get; set; }

        #endregion

        #region class

        /// <summary>
        /// The day of the week the class meets, such as Wednesday.
        /// </summary>
        public string MeetsOn { get; set; }

        /// <summary>
        /// How many weekly sessions one class has. A class is complete on its last session.
        /// </summary>
        public int Sessions { get; set; }

        /// <summary>
        /// How many weeks apart classes start.
        /// </summary>
        public int EveryWeeks { get; set; }

        /// <summary>
        /// The current class started this many weeks before the install day (its meeting day on or before then).
        /// </summary>
        public int CurrentStartedWeeksAgo { get; set; }

        public IntRange InProgressPerCampus { get; set; } = new IntRange();

        /// <summary>
        /// Step type keys whose holders fill the class's share first.
        /// </summary>
        public List<string> Prefer { get; set; } = new List<string>();

        /// <summary>
        /// For a class, the share of active adults who take it. For baptism, the share of generated active adults baptized.
        /// </summary>
        public double PercentOfActiveAdults { get; set; }

        /// <summary>
        /// A step type key whose holders must all have completed this class before their first step of that type.
        /// </summary>
        public string RequiredBefore { get; set; }

        /// <summary>
        /// A step type key that must be complete before this class starts.
        /// </summary>
        public string After { get; set; }

        /// <summary>
        /// Dates the class near the person's first step of these types.
        /// </summary>
        public ClassNear Near { get; set; }

        /// <summary>
        /// Dates the class for people with none of the <see cref="Near"/> steps.
        /// </summary>
        public ClassOtherwise Otherwise { get; set; }

        /// <summary>
        /// The youngest a person can be on the step's date. Used by classes and baptism.
        /// </summary>
        public int MinimumAge { get; set; }

        #endregion

        #region baptism

        public Guid BaptismDateAttributeGuid { get; set; }

        public Guid BaptizedHereStepAttributeGuid { get; set; }

        public Guid BaptizedHerePersonAttributeGuid { get; set; }

        public double VolunteersAndLeadersPercent { get; set; }

        public double PercentOfOtherAdults { get; set; }

        public double BaptizedHerePercent { get; set; }

        public double BaptismSundayPercent { get; set; }

        public MonthWeek SummerSunday { get; set; }

        public int HereMinimumAge { get; set; }

        public int YearsBack { get; set; }

        #endregion

        #region giving-journey

        /// <summary>
        /// The journey stage name that starts a step.
        /// </summary>
        public string StartsAt { get; set; }

        /// <summary>
        /// The journey stage names that end a step.
        /// </summary>
        public List<string> EndsAt { get; set; } = new List<string>();

        #endregion
    }

    internal sealed class ClassNear
    {
        public List<string> StepTypes { get; set; } = new List<string>();

        /// <summary>
        /// The class is complete in these days before the first such step. Zero when <see cref="DaysAfter"/> is used.
        /// </summary>
        public int DaysBefore { get; set; }

        /// <summary>
        /// The class starts in these days after the first such step. Zero when <see cref="DaysBefore"/> is used.
        /// </summary>
        public int DaysAfter { get; set; }
    }

    internal sealed class ClassOtherwise
    {
        /// <summary>
        /// The class starts within these days of the <see cref="StepSource.After"/> step's completion, or of the
        /// household's first visit when there is no such step.
        /// </summary>
        public int DaysAfter { get; set; }
    }

    internal sealed class MonthWeek
    {
        public int Month { get; set; }

        /// <summary>
        /// Which Sunday of the month, from 1.
        /// </summary>
        public int Week { get; set; }
    }
}
