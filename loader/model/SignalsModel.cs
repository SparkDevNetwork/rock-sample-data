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
    /// configuration/signals.json: the church's person signal types, the Cast's signals, and the Crowd's Care Team
    /// Follow-ups (issue 45).
    /// </summary>
    internal sealed class SignalsConfiguration
    {
        /// <summary>
        /// The signal types, in the order Rock's editor gives them. The first is the top signal when a person has several.
        /// </summary>
        public List<SignalTypeDefinition> Types { get; set; } = new List<SignalTypeDefinition>();

        public List<CastSignal> Cast { get; set; } = new List<CastSignal>();

        public CrowdSignals Crowd { get; set; }

        /// <summary>
        /// Questions verify asks Rock's own security check about the signal types.
        /// </summary>
        public List<AccessExpectation> Access { get; set; } = new List<AccessExpectation>();

        public SignalTypeDefinition Type( string key )
        {
            return Types.FirstOrDefault( t => t.Key == key )
                ?? throw new InvalidDataException( $"configuration/signals.json names signal type '{key}', which is not one of its types." );
        }
    }

    internal sealed class SignalTypeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The flag's color, as Rock's color picker saves it.
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// The icon's CSS class, such as "ti ti-car".
        /// </summary>
        public string Icon { get; set; }

        public string Description { get; set; }

        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();
    }

    /// <summary>
    /// One Cast person's signal.
    /// </summary>
    internal sealed class CastSignal
    {
        public Guid Guid { get; set; }

        public Guid Person { get; set; }

        /// <summary>
        /// The person's name, for messages.
        /// </summary>
        public string Name { get; set; }

        public string Type { get; set; }

        /// <summary>
        /// The staff member who follows up.
        /// </summary>
        public Guid Owner { get; set; }

        /// <summary>
        /// Who added the signal, when that is not the owner.
        /// </summary>
        public Guid? AddedBy { get; set; }

        public string Note { get; set; }

        public int AddedDaysAgo { get; set; }

        /// <summary>
        /// Days after install day the signal ends, or null.
        /// </summary>
        public int? EndsInDays { get; set; }

        /// <summary>
        /// Days before install day the signal ended, or null.
        /// </summary>
        public int? EndedDaysAgo { get; set; }
    }

    /// <summary>
    /// The Crowd's signals: each campus with generated households gets its campus pastor's follow-ups.
    /// </summary>
    internal sealed class CrowdSignals
    {
        public string Type { get; set; }

        public int PerCampus { get; set; }

        /// <summary>
        /// How many of each campus's signals are open with no end date.
        /// </summary>
        public int OpenWithoutEnd { get; set; }

        /// <summary>
        /// How many of each campus's signals are open and end after install day. The rest have ended.
        /// </summary>
        public int OpenWithEnd { get; set; }

        /// <summary>
        /// The connection statuses of the adults who can get one: people their campus pastor knows.
        /// </summary>
        public List<string> ConnectionStatuses { get; set; } = new List<string>();

        public DayRange OpenAddedDaysAgo { get; set; }

        public DayRange EndsInDays { get; set; }

        public DayRange EndedDaysAgo { get; set; }

        public DayRange AddedDaysBeforeEnd { get; set; }

        public List<string> Notes { get; set; } = new List<string>();
    }

    internal sealed class DayRange
    {
        public int Min { get; set; }

        public int Max { get; set; }
    }
}
