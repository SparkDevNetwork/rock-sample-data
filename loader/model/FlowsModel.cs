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
        These classes mirror configuration/flows.json. Property names match the JSON keys
        (case-insensitive).
    */

    /// <summary>
    /// The contents of configuration/flows.json: the church's Communication Flows.
    /// </summary>
    internal sealed class FlowsConfiguration
    {
        /// <summary>
        /// The one email domain that can never reach a real person. Only people whose email is on it, or who have no
        /// email, are ever in a flow run that is still open on install day (decision 220).
        /// </summary>
        public string ReservedEmailDomain { get; set; } = "example.org";

        public List<FlowDefinition> Flows { get; set; } = new List<FlowDefinition>();
    }

    /// <summary>
    /// One Communication Flow.
    /// </summary>
    internal sealed class FlowDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The name Rock's unsubscribe and email preference pages show.
        /// </summary>
        public string PublicName { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The flow's category key in configuration/categories.json: its Ministry by Design system.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// onDemand. Recurring and one-time flows come in later slices.
        /// </summary>
        public string Trigger { get; set; }

        public FlowEntry Entry { get; set; }

        /// <summary>
        /// How many months back the flow's runs reach from install day.
        /// </summary>
        public int HistoryMonths { get; set; }

        /// <summary>
        /// The messages in path order.
        /// </summary>
        public List<FlowMessage> Messages { get; set; } = new List<FlowMessage>();

        public FlowGoal Goal { get; set; }

        /// <summary>
        /// lastMessageSent, anyEmailOpened, anyEmailClicked, or conversionAchieved.
        /// </summary>
        public string Exit { get; set; }
    }

    /// <summary>
    /// How a person enters an On-Demand flow.
    /// </summary>
    internal sealed class FlowEntry
    {
        /// <summary>
        /// The step type a person completes, by its key in configuration/steps.json. They join the run that starts the
        /// day after the step is completed.
        /// </summary>
        public string StepType { get; set; }

        /// <summary>
        /// Leaves out people who already met the flow's goal, as a workflow that adds people would check first. After
        /// Foundations asks people to serve, so it skips anyone already serving.
        /// </summary>
        public bool SkipIfGoalMet { get; set; }
    }

    /// <summary>
    /// One message in a flow.
    /// </summary>
    internal sealed class FlowMessage
    {
        /// <summary>
        /// The message's key in the library: an email in emails.json, or a text in texts.json.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The message's name in the flow, which Rock adds to the flow's name for each send.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Days after the previous message, or after the run's start for the first.
        /// </summary>
        public int DaysToWait { get; set; }

        /// <summary>
        /// The time of day it goes out, as HH:mm.
        /// </summary>
        public string Time { get; set; }
    }

    /// <summary>
    /// A flow's conversion goal.
    /// </summary>
    internal sealed class FlowGoal
    {
        /// <summary>
        /// joinedGroupType or tookStep. The other goal types come in later slices.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// For joinedGroupType: small-groups or serving-teams.
        /// </summary>
        public string GroupType { get; set; }

        /// <summary>
        /// For tookStep: the step type's key in configuration/steps.json.
        /// </summary>
        public string StepType { get; set; }

        /// <summary>
        /// The goal's window, in days from the run's start date.
        /// </summary>
        public int Days { get; set; }

        public int TargetPercent { get; set; }
    }
}
