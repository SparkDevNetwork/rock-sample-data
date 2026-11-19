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
    /// configuration/step-workflows.json: the follow-up tasks Rock starts when a person completes a step (issue 18).
    /// </summary>
    internal sealed class StepWorkflowsConfiguration
    {
        /// <summary>
        /// The person who built the workflow types and triggers.
        /// </summary>
        public Guid Author { get; set; }

        /// <summary>
        /// The workflow types and triggers were built this many months before the install day. Every step of a triggered type
        /// completed since has its workflow.
        /// </summary>
        public int BuiltMonthsAgo { get; set; }

        /// <summary>
        /// A workflow whose step was completed in this many days before the install day still waits at its form.
        /// </summary>
        public int OpenDays { get; set; }

        public List<StepWorkflowDefinition> Workflows { get; set; } = new List<StepWorkflowDefinition>();

        public List<CastStepWorkflow> Cast { get; set; } = new List<CastStepWorkflow>();
    }

    /// <summary>
    /// One workflow type and the step type that starts it.
    /// </summary>
    internal sealed class StepWorkflowDefinition
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The key of its category in configuration/categories.json.
        /// </summary>
        public string Category { get; set; }

        public string WorkTerm { get; set; }

        public string IconCssClass { get; set; }

        public string Prefix { get; set; }

        /// <summary>
        /// The key of the step type in configuration/steps.json.
        /// </summary>
        public string StepType { get; set; }

        /// <summary>
        /// complete (Rock's Step Completed trigger) or manual.
        /// </summary>
        public string Trigger { get; set; }

        public StepWorkflowAttribute Person { get; set; }

        public StepWorkflowAssignee Assignee { get; set; }

        public StepWorkflowForm Form { get; set; }

        public List<StepWorkflowField> Fields { get; set; } = new List<StepWorkflowField>();

        /// <summary>
        /// How many days after it started the assignee completed a past task.
        /// </summary>
        public DayRange CompletedDaysAfter { get; set; }

        public bool IsManual => Trigger == "manual";
    }

    /// <summary>
    /// A workflow attribute's key, name, and description.
    /// </summary>
    internal class StepWorkflowAttribute
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }
    }

    /// <summary>
    /// Who gets the task, held in a Person attribute: the leader of the step's campus, a staff member, or whoever starts it.
    /// </summary>
    internal sealed class StepWorkflowAssignee : StepWorkflowAttribute
    {
        public bool CampusLeader { get; set; }

        /// <summary>
        /// A person by Guid.
        /// </summary>
        public Guid? Staff { get; set; }

        public bool CurrentPerson { get; set; }
    }

    internal sealed class StepWorkflowForm
    {
        public string Header { get; set; }

        public string Button { get; set; }

        public string Response { get; set; }
    }

    /// <summary>
    /// A field the assignee fills in on the form.
    /// </summary>
    internal sealed class StepWorkflowField
    {
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// person, boolean, or memo.
        /// </summary>
        public string Type { get; set; }

        public bool Required { get; set; }

        public StepWorkflowFieldHistory History { get; set; }
    }

    /// <summary>
    /// What a completed task holds in a field.
    /// </summary>
    internal sealed class StepWorkflowFieldHistory
    {
        /// <summary>
        /// The person who did the task.
        /// </summary>
        public bool Assignee { get; set; }

        public double? YesPercent { get; set; }

        /// <summary>
        /// Yes when the person was baptized after the step, and yesPercent otherwise.
        /// </summary>
        public bool IfBaptizedAfter { get; set; }

        /// <summary>
        /// The share of tasks with a note.
        /// </summary>
        public double? Percent { get; set; }

        public List<string> Pool { get; set; } = new List<string>();
    }

    /// <summary>
    /// A Cast person's step workflow and what Rock shows for it.
    /// </summary>
    internal sealed class CastStepWorkflow
    {
        public string Key { get; set; }

        /// <summary>
        /// The workflow's key.
        /// </summary>
        public string Workflow { get; set; }

        public Guid Person { get; set; }

        public string PersonName { get; set; }

        /// <summary>
        /// open or complete.
        /// </summary>
        public string Expect { get; set; }

        public int? CompletedDaysAfter { get; set; }

        /// <summary>
        /// The completed task's field values by key. A person field holds a person's Guid.
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }
}
