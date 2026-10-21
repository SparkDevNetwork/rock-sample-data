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

namespace RockSampleData.Loader
{
    /// <summary>
    /// One line of a load's progress, as the loader CLI prints it.
    /// </summary>
    public sealed class LoadProgressReport
    {
        internal LoadProgressReport( string message, string stepName, int completedSteps, int totalSteps )
        {
            Message = message;
            StepName = stepName;
            CompletedSteps = completedSteps;
            TotalSteps = totalSteps;
        }

        /// <summary>
        /// The line, for example "Giving" or "  Gifts: 2,836 ($612,040) in 380 batches". Detail lines start with spaces.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// The step that starts with this line, for example "People" or "Giving". Null for a detail line.
        /// </summary>
        public string StepName { get; }

        /// <summary>
        /// How many steps have finished.
        /// </summary>
        public int CompletedSteps { get; }

        /// <summary>
        /// How many steps this load runs, after part selection.
        /// </summary>
        public int TotalSteps { get; }
    }

    /// <summary>
    /// Where the loader reports its progress. It reports on the loading thread, one line at a time, and
    /// never writes to the console.
    /// </summary>
    internal sealed class LoadLog
    {
        private readonly IProgress<LoadProgressReport> _progress;

        public LoadLog( IProgress<LoadProgressReport> progress )
        {
            _progress = progress;
        }

        /// <summary>
        /// How many steps this load runs.
        /// </summary>
        public int TotalSteps { get; set; }

        /// <summary>
        /// How many steps have finished.
        /// </summary>
        public int CompletedSteps { get; private set; }

        /// <summary>
        /// Reports a detail line.
        /// </summary>
        public void Write( string message )
        {
            _progress?.Report( new LoadProgressReport( message, null, CompletedSteps, TotalSteps ) );
        }

        /// <summary>
        /// Reports the line that starts a step.
        /// </summary>
        public void StartStep( string name )
        {
            _progress?.Report( new LoadProgressReport( name, name, CompletedSteps, TotalSteps ) );
        }

        /// <summary>
        /// Counts a step as finished.
        /// </summary>
        public void FinishStep()
        {
            CompletedSteps++;
        }
    }
}
