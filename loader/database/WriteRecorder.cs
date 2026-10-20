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

namespace RockSampleData.Loader.Database
{
    /// <summary>
    /// Records what a load touches in Rock: every column it writes, every Guid it looks up, and every
    /// stored procedure it runs. The verify tool turns a recording into the contract (validation/rock-contract.json),
    /// so the contract always comes from what the loader really does, never from a hand-kept list.
    /// </summary>
    internal sealed class WriteRecorder
    {
        private static readonly Regex GuidLiteral = new Regex( @"'([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})'" );
        private static readonly Regex Procedure = new Regex( @"\bEXEC(?:UTE)?\s+(?:\[dbo\]\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase );
        internal static readonly Regex WriteKeyword = new Regex( @"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\b", RegexOptions.IgnoreCase );

        /// <summary>
        /// Columns written, by Rock table. Temp tables are left out.
        /// </summary>
        public SortedDictionary<string, SortedSet<string>> Columns { get; } = new SortedDictionary<string, SortedSet<string>>( StringComparer.Ordinal );

        /// <summary>
        /// Rock tables the load deletes rows from (its own starter rows, before it writes new ones).
        /// </summary>
        public SortedSet<string> Deletes { get; } = new SortedSet<string>( StringComparer.Ordinal );

        /// <summary>
        /// Every Guid the load looked up or named, by the table it was looked up in when known.
        /// The verify tool keeps only the ones Rock itself provides.
        /// </summary>
        public HashSet<Guid> Guids { get; } = new HashSet<Guid>();

        /// <summary>
        /// Rock's stored procedures the load runs.
        /// </summary>
        public SortedSet<string> Procedures { get; } = new SortedSet<string>( StringComparer.Ordinal );

        /// <summary>
        /// Write statements the parser could not read. A recording with any of these cannot become a contract.
        /// </summary>
        public List<string> Unparsed { get; } = new List<string>();

        /// <summary>
        /// Records a bulk copy's columns.
        /// </summary>
        public void RecordBulk( string table, IEnumerable<string> columns )
        {
            if ( !table.StartsWith( "#" ) )
            {
                AddColumns( table, columns );
            }
        }

        /// <summary>
        /// Records a command: the columns its write statements set, the Guids it names, and the procedures it runs.
        /// </summary>
        public void RecordCommand( SqlCommand command )
        {
            var sql = command.CommandText;
            foreach ( Match match in GuidLiteral.Matches( sql ) )
            {
                Guids.Add( Guid.Parse( match.Groups[1].Value ) );
            }

            foreach ( SqlParameter parameter in command.Parameters )
            {
                if ( parameter.Value is Guid guid )
                {
                    Guids.Add( guid );
                }
            }

            foreach ( Match match in Procedure.Matches( sql ) )
            {
                Procedures.Add( match.Groups[1].Value );
            }

            // Every write keyword must be one write the parser read. Any other count means it missed or misread one.
            var keywords = WriteKeyword.Matches( sql ).Count;
            if ( keywords == 0 )
            {
                return;
            }

            var writes = SqlWrites.Parse( sql );
            if ( writes == null || writes.Count != keywords )
            {
                Unparsed.Add( sql.Trim() );
                return;
            }

            foreach ( var write in writes.Where( w => !w.Table.StartsWith( "#" ) ) )
            {
                if ( write.Columns.Count == 0 )
                {
                    Deletes.Add( write.Table );
                }
                else
                {
                    AddColumns( write.Table, write.Columns );
                }
            }
        }

        private void AddColumns( string table, IEnumerable<string> columns )
        {
            if ( !Columns.TryGetValue( table, out var set ) )
            {
                set = new SortedSet<string>( StringComparer.Ordinal );
                Columns[table] = set;
            }

            set.UnionWith( columns );
        }
    }

    /// <summary>
    /// Reads which table columns a T-SQL batch writes. It understands the loader's own statements:
    /// INSERT INTO [Table] ( [columns] ), UPDATE [Table] SET [column] = ..., UPDATE [alias] SET ... FROM [Table] AS [alias],
    /// and DELETE FROM [Table]. A delete comes back with no columns.
    /// </summary>
    internal static class SqlWrites
    {
        private static readonly Regex Insert = new Regex( @"\bINSERT\s+INTO\s+(\[?[\w#]+\]?)\s*\(", RegexOptions.IgnoreCase );
        private static readonly Regex Update = new Regex( @"\bUPDATE\s+(\[?[\w#]+\]?)\s+SET\b", RegexOptions.IgnoreCase );
        private static readonly Regex Delete = new Regex( @"\bDELETE\s+(?:FROM\s+)?(\[?[\w#]+\]?)", RegexOptions.IgnoreCase );
        private static readonly Regex Merge = new Regex( @"\bMERGE\b", RegexOptions.IgnoreCase );
        private static readonly Regex Alias = new Regex( @"(?:FROM|JOIN)\s+(\[?[\w#]+\]?)\s+(?:AS\s+)?\[(\w+)\]", RegexOptions.IgnoreCase );
        private static readonly Regex Target = new Regex( @"^\s*(?:\[\w+\]\.)?\[(\w+)\]\s*=", RegexOptions.None );
        private static readonly Regex Bracketed = new Regex( @"\[(\w+)\]" );

        /// <summary>
        /// Returns each written table with its columns, or null when the batch has a write the parser does not understand.
        /// </summary>
        public static List<(string Table, List<string> Columns)> Parse( string sql )
        {
            // The loader never merges. A batch that does needs a person to read it.
            if ( Merge.IsMatch( sql ) )
            {
                return null;
            }

            var writes = new List<(string, List<string>)>();

            foreach ( Match match in Insert.Matches( sql ) )
            {
                var list = ReadParenthesized( sql, match.Index + match.Length - 1 );
                var columns = Bracketed.Matches( list ).Cast<Match>().Select( m => m.Groups[1].Value ).ToList();
                if ( columns.Count == 0 )
                {
                    return null;
                }

                writes.Add( (Unbracket( match.Groups[1].Value ), columns) );
            }

            foreach ( Match match in Update.Matches( sql ) )
            {
                var table = Resolve( sql, match.Index, Unbracket( match.Groups[1].Value ) );
                var columns = new List<string>();
                foreach ( var assignment in SplitTopLevel( ReadSetList( sql, match.Index + match.Length ) ) )
                {
                    var target = Target.Match( assignment );
                    if ( !target.Success )
                    {
                        return null;
                    }

                    columns.Add( target.Groups[1].Value );
                }

                if ( columns.Count == 0 )
                {
                    return null;
                }

                writes.Add( (table, columns) );
            }

            foreach ( Match match in Delete.Matches( sql ) )
            {
                writes.Add( (Resolve( sql, match.Index, Unbracket( match.Groups[1].Value ) ), new List<string>()) );
            }

            return writes.Count == 0 ? null : writes;
        }

        /// <summary>
        /// Turns a name used by an UPDATE or DELETE into its table: an alias is looked up only in the same statement,
        /// which runs until the next write keyword, so an alias reused elsewhere in the batch cannot mislead it.
        /// </summary>
        private static string Resolve( string sql, int start, string name )
        {
            var next = WriteRecorder.WriteKeyword.Match( sql, start + 1 );
            var statement = sql.Substring( start, ( next.Success ? next.Index : sql.Length ) - start );
            var alias = Alias.Matches( statement ).Cast<Match>().FirstOrDefault( m => string.Equals( m.Groups[2].Value, name, StringComparison.OrdinalIgnoreCase ) );
            return alias == null ? name : Unbracket( alias.Groups[1].Value );
        }

        private static string Unbracket( string name )
        {
            return name.Trim( '[', ']' );
        }

        /// <summary>
        /// Returns the text inside the parentheses that open at the given index.
        /// </summary>
        private static string ReadParenthesized( string sql, int open )
        {
            var depth = 0;
            for ( var i = open; i < sql.Length; i++ )
            {
                depth += sql[i] == '(' ? 1 : sql[i] == ')' ? -1 : 0;
                if ( depth == 0 )
                {
                    return sql.Substring( open + 1, i - open - 1 );
                }
            }

            return sql.Substring( open + 1 );
        }

        /// <summary>
        /// Returns a SET list: everything up to the first FROM, WHERE, or statement end outside parentheses and strings.
        /// </summary>
        private static string ReadSetList( string sql, int start )
        {
            var depth = 0;
            var inString = false;
            for ( var i = start; i < sql.Length; i++ )
            {
                var c = sql[i];
                if ( c == '\'' )
                {
                    inString = !inString;
                }
                else if ( !inString )
                {
                    depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                    var isKeyword = depth == 0 && ( StartsWord( sql, i, "FROM" ) || StartsWord( sql, i, "WHERE" ) );
                    if ( isKeyword || ( depth == 0 && c == ';' ) )
                    {
                        return sql.Substring( start, i - start );
                    }
                }
            }

            return sql.Substring( start );
        }

        private static bool StartsWord( string sql, int index, string word )
        {
            var isStart = index == 0 || !char.IsLetterOrDigit( sql[index - 1] );
            var fits = index + word.Length <= sql.Length && string.Compare( sql, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase ) == 0;
            var isEnd = index + word.Length == sql.Length || !char.IsLetterOrDigit( sql[index + word.Length] );
            return isStart && fits && isEnd;
        }

        /// <summary>
        /// Splits on commas that are outside parentheses and strings.
        /// </summary>
        private static IEnumerable<string> SplitTopLevel( string text )
        {
            var depth = 0;
            var inString = false;
            var start = 0;
            for ( var i = 0; i < text.Length; i++ )
            {
                var c = text[i];
                if ( c == '\'' )
                {
                    inString = !inString;
                }
                else if ( !inString )
                {
                    depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                    if ( c == ',' && depth == 0 )
                    {
                        yield return text.Substring( start, i - start );
                        start = i + 1;
                    }
                }
            }

            if ( text.Substring( start ).Trim().Length > 0 )
            {
                yield return text.Substring( start );
            }
        }
    }
}
