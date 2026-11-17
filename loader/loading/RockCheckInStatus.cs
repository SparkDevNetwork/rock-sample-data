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

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        A port of the rule in Rock's attendance save hook (Rock/Model/Event/Attendance/Attendance.SaveHook.cs,
        UpdateCheckInStatusFromCheckInDates). When Rock adds an attendance row with an Unknown status, it sets the
        status from the row's dates: Checked Out with an end time, Present with a present time, and Not Present
        otherwise. Rock 20 and Rock 21 have the same rule. The loader writes no end times, so its rows are Present
        or Not Present (decision 260).

        Reason: Check-in screens read the status to show who is in a room, and Rock never leaves it Unknown.
    */

    /// <summary>
    /// The check-in status Rock's attendance save hook gives a new row (Rock's CheckInStatus enum).
    /// </summary>
    internal static class RockCheckInStatus
    {
        public const int NotPresent = 2;

        public const int Present = 3;

        /// <summary>
        /// The status of a row with no end time: Present when it has a present time, otherwise Not Present.
        /// </summary>
        public static int For( bool hasPresentTime ) => hasPresentTime ? Present : NotPresent;
    }
}
