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
        10/22/26 - CLAUDE

        Rock's Pledge Analytics shows only a percent complete. These states are the sample data's words for
        how a pledge is going on install day, so the Cast's stories and the Crowd's mix can be checked. A
        pledge is on pace within one gift of where it should be, or within a tenth of the pledge for frequent
        givers, so a quarterly giver one check early is still on pace.

        Reason: The Cast's pledge stories are checked on every load (docs/pledges.md).
    */

    /// <summary>
    /// The state of a pledge on a given day, from the pledge and the campaign gifts its giving unit gave.
    /// </summary>
    internal static class PledgeProgress
    {
        public const string Complete = "complete";
        public const string Ahead = "ahead";
        public const string OnPace = "on-pace";
        public const string Behind = "behind";
        public const string Stopped = "stopped";
        public const string NotStarted = "not-started";
        public const string GiftsOnly = "gifts-only";
        public const string None = "none";

        /// <summary>
        /// A giver who gave nothing to the campaign for this long, while behind, has stopped.
        /// </summary>
        public const int StoppedAfterDays = 60;

        /// <summary>
        /// How many gifts a year each pledge frequency asks for. A pledge with no frequency, or a one-time
        /// pledge, is one gift.
        /// </summary>
        private static readonly Dictionary<string, int> GiftsPerYear = new Dictionary<string, int>
        {
            ["weekly"] = 52,
            ["bi-weekly"] = 26,
            ["monthly"] = 12,
            ["quarterly"] = 4,
            ["yearly"] = 1
        };

        /// <summary>
        /// The pledge's state on <paramref name="today"/>. A null total means campaign gifts with no pledge.
        /// </summary>
        public static string State( decimal? total, string frequency, DateTime start, DateTime end, decimal given, DateTime? lastGift, DateTime today )
        {
            if ( total == null )
            {
                return given > 0 ? GiftsOnly : None;
            }

            if ( given >= total )
            {
                return Complete;
            }

            if ( given <= 0 )
            {
                return NotStarted;
            }

            var length = Math.Max( 1, ( end - start ).TotalDays );
            var expected = total.Value * ( decimal ) Math.Clamp( ( today - start ).TotalDays / length, 0, 1 );
            var gifts = GiftsPerYear.TryGetValue( frequency ?? string.Empty, out var perYear ) ? Math.Max( 1, perYear * length / 365.25 ) : 1;
            var tolerance = Math.Min( total.Value / 4, Math.Max( total.Value / ( decimal ) gifts, total.Value / 10 ) );

            if ( given >= expected + tolerance )
            {
                return Ahead;
            }

            if ( given > expected - tolerance )
            {
                return OnPace;
            }

            return lastGift.HasValue && ( today - lastGift.Value ).TotalDays > StoppedAfterDays ? Stopped : Behind;
        }
    }
}
