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
    /// configuration/logins.json: who among generated adults has a Rock Database login (decision 179).
    /// The Cast's logins are hand-written in cast/households.
    /// </summary>
    internal sealed class LoginsConfiguration
    {
        /// <summary>
        /// The youngest age with a login.
        /// </summary>
        public int MinimumAge { get; set; } = 18;

        /// <summary>
        /// Each reason a person might have a login, with its chance. A person takes the chance of their strongest reason.
        /// </summary>
        public List<LoginReason> Reasons { get; set; } = new List<LoginReason>();

        /// <summary>
        /// The connection statuses whose adults take the "connected" reason.
        /// </summary>
        public List<string> Connected { get; set; } = new List<string>();

        /// <summary>
        /// The connection statuses whose adults take the "known" reason.
        /// </summary>
        public List<string> Known { get; set; } = new List<string>();

        /// <summary>
        /// The connection statuses whose adults take the "prospect" reason.
        /// </summary>
        public List<string> Prospect { get; set; } = new List<string>();

        /// <summary>
        /// The chance for an adult whose record is inactive, in place of their other reasons, except the app and campus pastors.
        /// </summary>
        public double InactivePercent { get; set; }

        /// <summary>
        /// Older adults take a share of their chance, except where a reason is certain.
        /// </summary>
        public OlderAdultLogins OlderAdults { get; set; } = new OlderAdultLogins();

        /// <summary>
        /// The share of generated logins that were never confirmed and never used.
        /// </summary>
        public double UnconfirmedPercent { get; set; }

        /// <summary>
        /// The chance for a reason, in percent.
        /// </summary>
        /// <exception cref="InvalidDataException">configuration/logins.json does not list the reason.</exception>
        public double Percent( string key )
        {
            return Reasons.FirstOrDefault( r => r.Key == key )?.Percent
                ?? throw new InvalidDataException( $"configuration/logins.json has no '{key}' reason." );
        }
    }

    /// <summary>
    /// One reason a person might have a login.
    /// </summary>
    internal sealed class LoginReason
    {
        public string Key { get; set; }

        public double Percent { get; set; }

        public string Why { get; set; }
    }

    /// <summary>
    /// From what age, and by what factor, an adult's chance of a login drops.
    /// </summary>
    internal sealed class OlderAdultLogins
    {
        public int FromAge { get; set; } = 65;

        public double Factor { get; set; } = 1;
    }
}
