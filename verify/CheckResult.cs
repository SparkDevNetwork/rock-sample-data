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

namespace RockSampleData.Verify
{
    /// <summary>
    /// The outcome of one check, as it appears in a receipt.
    /// </summary>
    internal sealed class CheckResult
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        /// <summary>
        /// One line a person can read: what was checked and what was found.
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// Every problem, or for a passing check, anything a reviewer should still see.
        /// </summary>
        public List<string> Details { get; set; } = new List<string>();

        public static CheckResult From( string name, string summary, List<string> problems, List<string> notes = null )
        {
            return new CheckResult
            {
                Name = name,
                Passed = problems.Count == 0,
                Summary = summary,
                Details = problems.Concat( notes ?? new List<string>() ).ToList()
            };
        }

        public void Print()
        {
            Console.WriteLine( $"  {( Passed ? "PASS" : "FAIL" )} {Name}: {Summary}" );
            foreach ( var detail in Details.Take( 25 ) )
            {
                Console.WriteLine( $"       {detail}" );
            }

            if ( Details.Count > 25 )
            {
                Console.WriteLine( $"       ... and {Details.Count - 25} more (see the receipt)" );
            }
        }
    }
}
