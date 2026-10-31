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
using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

using RockSampleData.Loader.Database;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/31/26 - CLAUDE

        Rock's AttributeValue save hook (AttributeValue.UpdateValueAsProperties, the same in Rock 20 and 21)
        fills ValueAsDateTime, ValueAsNumeric, ValueAsBoolean, and ValueAsPersonId from Value on every save,
        and a data view filter on an attribute compares those columns, not Value. The loader's writers
        bulk-copy values around that hook, and most left the columns empty, so a filter such as Last Gave
        matched nobody in Rock. Apply runs the hook once over every value the load wrote, after every part,
        so no writer has to repeat it. A statement that changes a value Rock already had sets the columns
        itself, with Parameters, since that row is Rock's and not the load's.

        Reason: decision D-comm-flows-slice4-3.
    */

    /// <summary>
    /// The columns Rock's AttributeValue save hook derives from Value.
    /// </summary>
    internal static class AttributeValueHook
    {
        private static readonly CultureInfo RockCulture = CultureInfo.GetCultureInfo( "en-US" );
        private static readonly Guid PersonFieldTypeGuid = new Guid( "E4EAB7B2-0B76-429B-AFE4-AD86D7428C70" );
        private static readonly DateTime SqlMinDate = new DateTime( 1753, 1, 1 );
        private static readonly string[] MonthDayFormats = { "MM/dd/yyyy", "M/dd/yyyy", "M/d/yyyy", "MM/d/yyyy" };
        private static readonly string[] TrueValues = { "1", "y", "t", "yes", "true" };
        private const decimal NumericLimit = 9_999_999_999_999_999.99m;

        /// <summary>
        /// The date, number, and boolean Rock's hook derives from a value, saved in <paramref name="year"/> (Rock reads a
        /// month and day such as "3/15" as that day of the current year).
        /// </summary>
        public static (decimal? Numeric, DateTime? DateTime, bool? Boolean) ValueAs( string value, int year )
        {
            var dateTime = AsDateTime( value, year );
            if ( dateTime.HasValue && dateTime.Value < SqlMinDate )
            {
                dateTime = null;
            }

            decimal? numeric = null;
            if ( value != null && value.Length <= 24 )
            {
                numeric = AsDecimalOrNull( value );
                if ( numeric.HasValue && Math.Abs( numeric.Value ) > NumericLimit )
                {
                    numeric = null;
                }
            }

            bool? boolean = !string.IsNullOrEmpty( value ) && value.Length <= 5 ? TrueValues.Contains( value.ToLowerInvariant() ) : null;
            return (numeric, dateTime, boolean);
        }

        /// <summary>
        /// The hook's columns as parameters @ValueAsNumeric, @ValueAsDateTime, and @ValueAsBoolean, for a statement that
        /// changes a value Rock already had.
        /// </summary>
        public static SqlParameter[] Parameters( string value, int year )
        {
            var (numeric, dateTime, boolean) = ValueAs( value, year );
            return new[] { P( "@ValueAsNumeric", ( object ) numeric ?? DBNull.Value ), P( "@ValueAsDateTime", ( object ) dateTime ?? DBNull.Value ), P( "@ValueAsBoolean", ( object ) boolean ?? DBNull.Value ) };
        }

        /// <summary>
        /// Runs Rock's hook over every value the load wrote, and returns how many it changed.
        /// </summary>
        public static int Apply( RockDatabase db, string foreignKey, DateTime anchor )
        {
            var personFieldTypeId = db.RequireIdByGuid( "FieldType", PersonFieldTypeGuid );
            var changes = TableFactory.Create( "Id:int", "ValueAsNumeric:decimal", "ValueAsDateTime:date", "ValueAsBoolean:bool", "ValueAsPersonId:int" );
            foreach ( var row in db.Query( @"
SELECT [av].[Id], [av].[Value], [av].[ValueAsNumeric], [av].[ValueAsDateTime], [av].[ValueAsBoolean], [av].[ValueAsPersonId], [pa].[PersonId] AS [AliasPersonId]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
LEFT JOIN [PersonAlias] AS [pa] ON [a].[FieldTypeId] = @PersonFieldTypeId AND [pa].[Guid] = TRY_CONVERT( UNIQUEIDENTIFIER, [av].[Value] )
WHERE [av].[ForeignKey] = @ForeignKey", P( "@PersonFieldTypeId", personFieldTypeId ), P( "@ForeignKey", foreignKey ) ) )
            {
                var (numeric, dateTime, boolean) = ValueAs( row["Value"] as string, anchor.Year );
                numeric = numeric.HasValue ? Math.Round( numeric.Value, 2, MidpointRounding.AwayFromZero ) : null;
                var personId = row["AliasPersonId"] as int?;
                var storedDate = row["ValueAsDateTime"] as DateTime?;
                var sameDate = storedDate.HasValue == dateTime.HasValue && ( !dateTime.HasValue || Math.Abs( ( storedDate.Value - dateTime.Value ).TotalMilliseconds ) < 4 );
                if ( !sameDate || row["ValueAsNumeric"] as decimal? != numeric || row["ValueAsBoolean"] as bool? != boolean || row["ValueAsPersonId"] as int? != personId )
                {
                    changes.Rows.Add( row["Id"], ( object ) numeric ?? DBNull.Value, ( object ) dateTime ?? DBNull.Value, ( object ) boolean ?? DBNull.Value, ( object ) personId ?? DBNull.Value );
                }
            }

            if ( changes.Rows.Count > 0 )
            {
                db.Execute( "CREATE TABLE #ValueAs ( [Id] INT PRIMARY KEY, [ValueAsNumeric] DECIMAL(18,2), [ValueAsDateTime] DATETIME, [ValueAsBoolean] BIT, [ValueAsPersonId] INT )" );
                db.BulkInsert( "#ValueAs", changes, keepIdentity: false );
                db.Execute( @"
UPDATE [av]
SET [av].[ValueAsNumeric] = [v].[ValueAsNumeric], [av].[ValueAsDateTime] = [v].[ValueAsDateTime], [av].[ValueAsBoolean] = [v].[ValueAsBoolean], [av].[ValueAsPersonId] = [v].[ValueAsPersonId]
FROM [AttributeValue] AS [av]
INNER JOIN #ValueAs AS [v] ON [v].[Id] = [av].[Id];
DROP TABLE #ValueAs;" );
            }

            return changes.Rows.Count;
        }

        /// <summary>
        /// Rock's string AsDateTime: only printable ASCII, a month and day as that day of the year, or a date in en-US.
        /// </summary>
        private static DateTime? AsDateTime( string value, int year )
        {
            if ( value == null )
            {
                return null;
            }

            var text = new string( value.Where( c => c > 31 && c < 127 ).ToArray() );
            if ( text.Length > 0 && text.Length <= 5 && text.Contains( '/' )
                && DateTime.TryParseExact( $"{text}/{year}", MonthDayFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var monthDay ) )
            {
                return monthDay;
            }

            return DateTime.TryParse( text, RockCulture, DateTimeStyles.None, out var parsed ) ? parsed : null;
        }

        /// <summary>
        /// Rock's string AsDecimalOrNull: one leading symbol, such as a currency sign, is dropped first.
        /// </summary>
        private static decimal? AsDecimalOrNull( string value )
        {
            var text = string.IsNullOrWhiteSpace( value ) ? value : Regex.Replace( value, @"^[^0-9\.-]", string.Empty );
            return decimal.TryParse( text, NumberStyles.Number, RockCulture, out var number ) ? number : null;
        }
    }
}
