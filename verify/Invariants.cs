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

using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

namespace RockSampleData.Verify
{
    /// <summary>
    /// Runs validation/invariants.sql against a loaded database. Every check must find zero rows that break its rule.
    /// </summary>
    internal static class Invariants
    {
        private static readonly Regex CheckStart = new Regex( @"^-- check: (\S+)\s*$", RegexOptions.Multiline );

        public static CheckResult Run( SqlServer sql, string database, string foreignKey, string invariantsPath )
        {
            var text = File.ReadAllText( invariantsPath );
            var starts = CheckStart.Matches( text ).Cast<Match>().ToList();
            var problems = new List<string>();
            for ( var i = 0; i < starts.Count; i++ )
            {
                var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
                var body = text.Substring( starts[i].Index, end - starts[i].Index );
                var why = Regex.Match( body, @"^-- why: (.+)$", RegexOptions.Multiline ).Groups[1].Value.Trim();
                var violations = Convert.ToInt64( sql.Scalar( database, body, new SqlParameter( "@ForeignKey", foreignKey ) ) );
                if ( violations > 0 )
                {
                    problems.Add( $"{starts[i].Groups[1].Value}: {violations:N0} rows break it. {why}" );
                }
            }

            return CheckResult.From( "invariants", $"{starts.Count - problems.Count} of {starts.Count} rules in validation/invariants.sql hold", problems );
        }
    }
}
