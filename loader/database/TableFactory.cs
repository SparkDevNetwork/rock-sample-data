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

using System.Data;

namespace RockSampleData.Loader.Database
{
    /// <summary>
    /// Builds the in-memory tables that are bulk-copied into Rock.
    /// </summary>
    internal static class TableFactory
    {
        /// <summary>
        /// Creates a DataTable from "Name:type" column specs (int, bool, guid, date, time, double, decimal, bytes, string).
        /// Column names must match the Rock table's column names.
        /// </summary>
        public static DataTable Create( params string[] columns )
        {
            var table = new DataTable();
            foreach ( var spec in columns )
            {
                var parts = spec.Split( ':' );
                var type = parts[1] switch
                {
                    "int" => typeof( int ),
                    "bool" => typeof( bool ),
                    "guid" => typeof( Guid ),
                    "date" => typeof( DateTime ),
                    "time" => typeof( TimeSpan ),
                    "double" => typeof( double ),
                    "decimal" => typeof( decimal ),
                    "bytes" => typeof( byte[] ),
                    _ => typeof( string )
                };
                table.Columns.Add( parts[0], type );
            }

            return table;
        }
    }
}
