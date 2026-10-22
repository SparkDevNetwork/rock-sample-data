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
using System.Text.Json.Nodes;

using Microsoft.Data.SqlClient;

using RockSampleData.Loader.Database;

using static RockSampleData.Loader.Database.RockDatabase;
using static RockSampleData.Loader.Loading.RockGivingAutomation;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Fills Rock's giving analytics as if Rock's Giving Automation job had run every night through the
    /// history window: each giver's giving attributes, their giving bin and percentile, and their journey
    /// stage with its history.
    /// </summary>
    /// <remarks>
    /// Rock does part of this work in two stored procedures that ship in every Rock database, and the loader
    /// calls those directly: bins and percentiles, and the final journey stage run. The rest lives in Rock's C#,
    /// so it is ported in <see cref="RockGivingAutomation"/>.
    /// </remarks>
    internal sealed class GivingAnalyticsLoader
    {
        private static readonly Guid ContributionGuid = new Guid( "2D607262-52D6-4724-910D-5C6E8FB89ACC" );
        private static readonly Guid GiverAnonymousGuid = new Guid( "802235DC-3CA5-94B0-4326-AACE71180F48" );
        private static readonly Guid AnonymousVisitorGuid = new Guid( "7EBC167B-512D-4683-9D80-98B6BB02E1B9" );
        private static readonly Guid NamelessRecordTypeGuid = new Guid( "721300ED-1267-4DA0-B4F2-6C6B5B17B1C5" );
        private static readonly Guid RestUserRecordTypeGuid = new Guid( "E2261A84-831D-4234-9BE0-4D628BBE751E" );

        private static readonly Guid FirstGaveGuid = new Guid( "EE5EC76A-D4B9-56B5-4B48-29627D945F10" );
        private static readonly Guid LastGaveGuid = new Guid( "02F64263-E290-399E-4487-FC236F4DE81F" );
        private static readonly Guid PercentScheduledGuid = new Guid( "98373264-0E65-4C79-B75B-4F8477AA647E" );
        private static readonly Guid PreferredSourceGuid = new Guid( "0567B279-1F4D-4573-9AA7-927A7278443E" );
        private static readonly Guid PreferredCurrencyGuid = new Guid( "77A5F7DE-9096-45C8-9051-9D8EE50E3C2F" );
        private static readonly Guid FrequencyMeanGuid = new Guid( "88E59B38-044C-4AE4-A455-A0D3A33DDEDA" );
        private static readonly Guid FrequencyStdDevGuid = new Guid( "1D5E4356-DC66-4067-BEF1-3560E61150BD" );
        private static readonly Guid FrequencyLabelGuid = new Guid( "1A58F7AA-238B-46E5-B1DC-0A5BC1F213A5" );
        private static readonly Guid NextExpectedGiftGuid = new Guid( "65D7CF79-BD80-44B2-9F5F-96D81B9B4990" );
        private static readonly Guid AmountMedianGuid = new Guid( "327F1CFF-A013-42B5-80A7-5922A40480EC" );
        private static readonly Guid AmountIqrGuid = new Guid( "CE129112-4BA9-4FC1-A67C-2A5C69140DA7" );
        private static readonly Guid LastClassifiedGuid = new Guid( "7220B230-03CE-4D1E-985B-26AA28BE06F8" );
        private static readonly Guid CurrentStageGuid = new Guid( "13C55AEA-6D88-4470-B3AE-EE5138F044DF" );
        private static readonly Guid PreviousStageGuid = new Guid( "B35CE867-6017-484E-9EC7-AEB93CD4B2D8" );
        private static readonly Guid StageChangeDateGuid = new Guid( "8FFE3554-43F2-40D8-8803-446559D2B1F7" );

        private const string SettingsKey = "core_GivingAutomationConfiguration";

        /// <summary>
        /// How many weekly journey stage runs the loader replays before the final one.
        /// </summary>
        private const int JourneyHistoryWeeks = 52;

        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly Dictionary<Guid, int> _attributeIds = new Dictionary<Guid, int>();

        public GivingAnalyticsLoader( RockDatabase db, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;

            // Rock's job runs every night at 10pm (its default schedule), so it last ran the night before the anchor.
            RunAt = dates.Anchor.Date.AddHours( -2 );
        }

        /// <summary>
        /// When Rock's job last ran, as far as the loaded data is concerned.
        /// </summary>
        public DateTime RunAt { get; }

        /// <summary>
        /// Rock's last weekly journey stage run: the last Tuesday at 10pm on or before <see cref="RunAt"/>. The stage
        /// Rock shows for each person is the one this run gave them.
        /// </summary>
        public DateTime FinalJourneyRun
        {
            get
            {
                var run = RunAt.Date.AddHours( 22 );
                while ( run.DayOfWeek != DayOfWeek.Tuesday || run > RunAt )
                {
                    run = run.AddDays( -1 );
                }

                return run;
            }
        }

        /// <summary>
        /// Every qualifying gift of this dataset's giving units, in date order, after <see cref="Load"/>.
        /// </summary>
        public Dictionary<string, List<RockGift>> Gifts { get; private set; }

        /// <summary>
        /// The giving attributes Rock's job writes when it classifies a unit, in the order the parity report lists them.
        /// </summary>
        private static readonly Guid[] ClassificationAttributes = { FirstGaveGuid, LastGaveGuid, PercentScheduledGuid, PreferredSourceGuid, PreferredCurrencyGuid,
            FrequencyMeanGuid, FrequencyStdDevGuid, FrequencyLabelGuid, NextExpectedGiftGuid, AmountMedianGuid, AmountIqrGuid, LastClassifiedGuid };

        /// <summary>
        /// The release-time parity test (see validation/README.md). Run it after Rock's own Giving Automation job
        /// has run on a loaded database with its filters marked as changed, so the job reclassified every giving
        /// unit. Recomputes each unit with the port as of the job's run time, compares every value Rock wrote, and
        /// returns the number of differences.
        /// </summary>
        public int CheckParity()
        {
            LoadAttributeIds();
            var settings = _db.Scalar( "SELECT [DefaultValue] FROM [Attribute] WHERE [Key] = @Key AND [EntityTypeQualifierColumn] = 'SystemSetting'", P( "@Key", SettingsKey ) ) as string;
            var lastRunText = ( string.IsNullOrWhiteSpace( settings ) ? null : JsonNode.Parse( settings ) )?["GivingClassificationSettings"]?["LastRunDateTime"]?.GetValue<string>();
            if ( lastRunText == null || !DateTimeOffset.TryParse( lastRunText, out var lastRunOffset ) )
            {
                _db.Log.Write( "  Rock's Giving Automation job has not recorded a run. Run it in Rock first." );
                return 1;
            }

            // The job's run time, as Rock wrote it (local clock time, no offset).
            var lastRun = lastRunOffset.DateTime;
            var lastRunValue = DateValue( lastRun );
            var gifts = QualifyingGifts();
            var definedValueGuids = _db.Query( "SELECT [Id], [Guid] FROM [DefinedValue]" ).ToDictionary( r => ( int ) r["Id"], r => ( Guid ) r["Guid"] );
            var stored = _db.Query( @"
SELECT [av].[EntityId], [av].[AttributeId], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
WHERE [p].[ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => (( int ) r["EntityId"], ( int ) r["AttributeId"]), r => ( string ) r["Value"] ?? string.Empty );
            string Stored( int personId, Guid attribute ) => stored.GetValueOrDefault( (personId, _attributeIds[attribute]) ) ?? string.Empty;

            var units = 0;
            var compared = 0;
            var differences = new List<string>();
            foreach ( var (givingId, personIds) in PeopleByGivingId() )
            {
                // Only the units Rock classified in that run.
                if ( Stored( personIds[0], LastClassifiedGuid ) != lastRunValue || !gifts.TryGetValue( givingId, out var unitGifts ) )
                {
                    continue;
                }

                units++;
                var expected = ClassificationValues( unitGifts, lastRun, definedValueGuids ).ToDictionary( v => v.Attribute, v => v.Value );

                // A unit with no gift in 12 months is cleared, except its first and last gift dates.
                var cleared = expected.Count == 0;
                foreach ( var personId in personIds )
                {
                    foreach ( var attribute in ClassificationAttributes.Where( a => !cleared || ( a != FirstGaveGuid && a != LastGaveGuid ) ) )
                    {
                        var want = attribute == LastClassifiedGuid ? lastRunValue : expected.GetValueOrDefault( attribute ) ?? string.Empty;
                        var have = Stored( personId, attribute );
                        compared++;
                        if ( want != have )
                        {
                            var key = _db.Scalar( "SELECT [Key] FROM [Attribute] WHERE [Id] = @Id", P( "@Id", _attributeIds[attribute] ) );
                            differences.Add( $"{givingId} (person {personId}) {key}: Rock wrote \"{have}\", the port computes \"{want}\"" );
                        }
                    }
                }
            }

            _db.Log.Write( $"  Rock's job ran at {lastRunValue}. Compared {compared:N0} values for {units:N0} giving units." );
            _db.Log.Write( differences.Count == 0 ? "  Parity: every value matches." : $"  Parity: {differences.Count:N0} of {compared:N0} values differ. The first ones:" );
            foreach ( var difference in differences.Take( 25 ) )
            {
                _db.Log.Write( $"    {difference}" );
            }

            return differences.Count;
        }

        private void LoadAttributeIds()
        {
            foreach ( var guid in ClassificationAttributes.Concat( new[] { CurrentStageGuid, PreviousStageGuid, StageChangeDateGuid } ) )
            {
                _attributeIds[guid] = _db.RequireIdByGuid( "Attribute", guid );
            }
        }

        /// <summary>
        /// Fills the giving analytics. <paramref name="gifts"/> are the dataset's qualifying gifts by giving id, in date
        /// order, as the giving step planned them; when null they are read from the database.
        /// </summary>
        public void Load( Dictionary<string, List<RockGift>> gifts = null )
        {
            var now = DateTime.Now;
            LoadAttributeIds();

            gifts ??= QualifyingGifts();
            Gifts = gifts;
            var people = PeopleByGivingId();
            var definedValueGuids = _db.Query( "SELECT [Id], [Guid] FROM [DefinedValue]" ).ToDictionary( r => ( int ) r["Id"], r => ( Guid ) r["Guid"] );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );

            void Add( IEnumerable<int> personIds, Guid attributeGuid, string value, DateTime created, DateTime modified )
            {
                foreach ( var personId in personIds )
                {
                    values.Rows.Add( false, _attributeIds[attributeGuid], personId, value, Guid.NewGuid(), created, modified, _foreignKey );
                }
            }

            var classified = 0;
            foreach ( var (givingId, personIds) in people )
            {
                if ( gifts.TryGetValue( givingId, out var unitGifts ) && Classify( unitGifts, definedValueGuids, ( guid, value, at ) => Add( personIds, guid, value, at, at ) ) )
                {
                    classified++;
                }
            }

            var predicted = ReplayJourneys( gifts, people, Add, out var finalRun );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );

            // The final runs are Rock's own stored procedures, so the end state is exactly Rock's.
            RunBinsAndPercentiles();
            var mismatches = RunFinalJourneyStages( finalRun, predicted );
            TagProcedureRows();
            SaveLastRunDateTime( now );

            _db.Log.Write( $"  Giving analytics: {classified:N0} giving units classified, {values.Rows.Count:N0} attribute values, journey stages replayed for {JourneyHistoryWeeks} weeks" );
            _db.Log.Write( mismatches == 0
                ? $"  Journey stage check: Rock's final run made the {predicted.Count:N0} changes the replay predicted, and no others"
                : $"  WARNING: Rock's final journey stage run differed from the replay for {mismatches:N0} people. Check RockGivingAutomation.GetJourneyStage against Rock." );
        }

        /// <summary>
        /// Every qualifying gift of this dataset's people, grouped by giving id and in date order. This is the
        /// filter Rock's job and procedures use (GetGivingAutomationSourceTransactionQuery): contributions to at
        /// least one tax-deductible account, with a positive line, that are not fully refunded. Each gift carries
        /// its amount per account with refunds applied, as Rock's FinancialTransactionView does.
        /// </summary>
        private Dictionary<string, List<RockGift>> QualifyingGifts()
        {
            var rows = _db.Query( @"
WITH [RefundTotals] AS (
    SELECT [ftr].[OriginalTransactionId], SUM( [ftd].[Amount] ) AS [Refunded]
    FROM [FinancialTransactionRefund] AS [ftr]
    INNER JOIN [FinancialTransactionDetail] AS [ftd] ON [ftd].[TransactionId] = [ftr].[Id]
    GROUP BY [ftr].[OriginalTransactionId]
),
[Qualifying] AS (
    SELECT
        [ft].[Id],
        [ft].[AuthorizedPersonAliasId],
        [p].[GivingId],
        [p].[PrimaryCampusId],
        [ft].[TransactionDateTime],
        [ft].[SourceTypeValueId],
        [fpd].[CurrencyTypeValueId],
        CASE WHEN [ft].[ScheduledTransactionId] IS NULL THEN 0 ELSE 1 END AS [IsScheduled]
    FROM [FinancialTransaction] AS [ft]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    LEFT JOIN [FinancialPaymentDetail] AS [fpd] ON [fpd].[Id] = [ft].[FinancialPaymentDetailId]
    LEFT JOIN [RefundTotals] AS [rt] ON [rt].[OriginalTransactionId] = [ft].[Id]
    WHERE [p].[ForeignKey] = @ForeignKey
        AND [p].[GivingId] IS NOT NULL AND [p].[GivingId] <> ''
        AND [p].[Guid] <> @GiverAnonymous
        AND [ft].[TransactionDateTime] IS NOT NULL
        AND [ft].[TransactionTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @Contribution )
        AND EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] INNER JOIN [FinancialAccount] AS [a] ON [a].[Id] = [d].[AccountId] WHERE [d].[TransactionId] = [ft].[Id] AND [a].[IsTaxDeductible] = 1 )
        AND EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] WHERE [d].[TransactionId] = [ft].[Id] AND [d].[Amount] > 0 )
        AND ( [rt].[OriginalTransactionId] IS NULL OR ( SELECT SUM( [d].[Amount] ) FROM [FinancialTransactionDetail] AS [d] WHERE [d].[TransactionId] = [ft].[Id] ) + [rt].[Refunded] <> 0 )
),
[Lines] AS (
    SELECT [d].[TransactionId], [d].[AccountId], [d].[Amount]
    FROM [FinancialTransactionDetail] AS [d]
    INNER JOIN [Qualifying] AS [q] ON [q].[Id] = [d].[TransactionId]
    UNION ALL
    SELECT [ftr].[OriginalTransactionId], [rd].[AccountId], [rd].[Amount]
    FROM [FinancialTransactionRefund] AS [ftr]
    INNER JOIN [FinancialTransactionDetail] AS [rd] ON [rd].[TransactionId] = [ftr].[Id]
    INNER JOIN [Qualifying] AS [q] ON [q].[Id] = [ftr].[OriginalTransactionId]
)
SELECT [q].*, [l].[AccountId], SUM( [l].[Amount] ) AS [Amount]
FROM [Qualifying] AS [q]
INNER JOIN [Lines] AS [l] ON [l].[TransactionId] = [q].[Id]
GROUP BY [q].[Id], [q].[AuthorizedPersonAliasId], [q].[GivingId], [q].[PrimaryCampusId], [q].[TransactionDateTime], [q].[SourceTypeValueId], [q].[CurrencyTypeValueId], [q].[IsScheduled], [l].[AccountId]
HAVING SUM( [l].[Amount] ) <> 0
ORDER BY [q].[GivingId], [q].[TransactionDateTime], [q].[Id], [l].[AccountId]",
                P( "@ForeignKey", _foreignKey ), P( "@GiverAnonymous", GiverAnonymousGuid ), P( "@Contribution", ContributionGuid ) );

            return rows
                .GroupBy( r => ( int ) r["Id"] )
                .Select( g =>
                {
                    var r = g.First();
                    return new RockGift( ( int ) r["Id"], ( int ) r["AuthorizedPersonAliasId"], ( string ) r["GivingId"], r["PrimaryCampusId"] as int?, ( DateTime ) r["TransactionDateTime"],
                        r["SourceTypeValueId"] as int?, r["CurrencyTypeValueId"] as int?, ( int ) r["IsScheduled"] == 1,
                        g.Select( l => (( int ) l["AccountId"], ( decimal ) l["Amount"]) ).ToList() );
                } )
                .GroupBy( gift => gift.GivingId )
                .ToDictionary( g => g.Key, g => g.ToList() );
        }

        /// <summary>
        /// This dataset's people who can carry giving values, by giving id. Rock leaves out nameless people and REST users.
        /// </summary>
        private Dictionary<string, List<int>> PeopleByGivingId()
        {
            return _db.Query( @"
SELECT [p].[Id], [p].[GivingId]
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND [p].[GivingId] IS NOT NULL AND [p].[GivingId] <> ''
    AND [p].[Guid] NOT IN ( @GiverAnonymous, @AnonymousVisitor )
    AND [p].[RecordTypeValueId] IS NOT NULL
    AND [p].[RecordTypeValueId] NOT IN ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] IN ( @Nameless, @RestUser ) )",
                P( "@ForeignKey", _foreignKey ), P( "@GiverAnonymous", GiverAnonymousGuid ), P( "@AnonymousVisitor", AnonymousVisitorGuid ),
                P( "@Nameless", NamelessRecordTypeGuid ), P( "@RestUser", RestUserRecordTypeGuid ) )
                .GroupBy( r => ( string ) r["GivingId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ( int ) r["Id"] ).ToList() );
        }

        /// <summary>
        /// Classifies one giving unit the way Rock's job did the night after its last gift
        /// (CalculateAttributesPerGivingId). Returns false when Rock would not have classified it yet.
        /// </summary>
        /// <remarks>
        /// Rock reclassifies a unit only on a night when it has a new gift. So a unit's values are as of the
        /// first 10pm run on or after its last gift; a lapsed giver keeps the values from back then, just as in
        /// a church that has run the job all along. A gift after the job's last run waits for tonight's run.
        /// </remarks>
        private bool Classify( List<RockGift> unitGifts, Dictionary<int, Guid> definedValueGuids, Action<Guid, string, DateTime> write )
        {
            var lastGift = unitGifts.Where( g => g.When <= RunAt ).Select( g => g.When ).DefaultIfEmpty().Max();
            if ( lastGift == default )
            {
                return false;
            }

            var classifiedAt = lastGift <= lastGift.Date.AddHours( 22 ) ? lastGift.Date.AddHours( 22 ) : lastGift.Date.AddDays( 1 ).AddHours( 22 );
            if ( classifiedAt > RunAt )
            {
                classifiedAt = RunAt;
            }

            foreach ( var (attributeGuid, value) in ClassificationValues( unitGifts, classifiedAt, definedValueGuids ) )
            {
                write( attributeGuid, value, classifiedAt );
            }

            return true;
        }

        /// <summary>
        /// The attribute values Rock's job writes for a giving unit when it classifies the unit at the given time
        /// (CalculateAttributesPerGivingId, plus the first gift). Empty when the unit gave nothing in the 12 months before.
        /// </summary>
        private static List<(Guid Attribute, string Value)> ClassificationValues( List<RockGift> unitGifts, DateTime classifiedAt, Dictionary<int, Guid> definedValueGuids )
        {
            var values = new List<(Guid, string)>();
            var oneYearAgo = classifiedAt.AddMonths( -12 );
            var transactions = unitGifts.Where( g => g.When >= oneYearAgo && g.When <= classifiedAt ).ToList();
            if ( transactions.Count == 0 )
            {
                return values;
            }

            var firstGiftDate = unitGifts[0].When;
            var hasEnoughDataForClassification = !( firstGiftDate > oneYearAgo && transactions.Count < MinimumTransactionCountForClassifications );

            values.Add( (FirstGaveGuid, DateValue( firstGiftDate )) );
            values.Add( (LastGaveGuid, DateValue( transactions.Max( t => t.When ) )) );
            values.Add( (PercentScheduledGuid, GetPercentScheduled( transactions ).ToString()) );

            var source = GetPreferred( transactions, t => t.SourceTypeValueId );
            if ( source.HasValue )
            {
                values.Add( (PreferredSourceGuid, definedValueGuids[source.Value].ToString()) );
            }

            var currency = GetPreferred( transactions, t => t.CurrencyTypeValueId );
            if ( currency.HasValue )
            {
                values.Add( (PreferredCurrencyGuid, definedValueGuids[currency.Value].ToString()) );
            }

            // Without enough data Rock writes these as blank; leaving them out is the same.
            if ( hasEnoughDataForClassification )
            {
                var frequency = GetFrequencyStats( transactions.Select( t => t.When ).OrderBy( d => d ).ToList(), TransactionWindowDurationHours );
                var (median, iqr) = GetMedianAndIqr( transactions.Select( t => t.TotalAmount ) );
                values.Add( (FrequencyMeanGuid, NumberValue( frequency.MeanDays )) );
                values.Add( (FrequencyStdDevGuid, NumberValue( frequency.StdDevDays )) );
                values.Add( (FrequencyLabelGuid, ( ( int ) frequency.Label ).ToString()) );
                if ( frequency.NextExpectedGiftDate.HasValue )
                {
                    values.Add( (NextExpectedGiftGuid, DateValue( frequency.NextExpectedGiftDate.Value )) );
                }

                values.Add( (AmountMedianGuid, NumberValue( median )) );
                values.Add( (AmountIqrGuid, NumberValue( iqr )) );
            }

            values.Add( (LastClassifiedGuid, DateValue( classifiedAt )) );
            return values;
        }

        /// <summary>
        /// Replays Rock's weekly journey stage runs (Tuesdays at 10pm) up to the week before the last one, and
        /// writes each person's current stage, previous stage, and the date it last changed. Returns the stage
        /// changes the last run should make, so Rock's own run can be checked against them.
        /// </summary>
        private Dictionary<int, int> ReplayJourneys( Dictionary<string, List<RockGift>> gifts, Dictionary<string, List<int>> people, Action<IEnumerable<int>, Guid, string, DateTime, DateTime> add, out DateTime finalRun )
        {
            finalRun = FinalJourneyRun;
            var lastRun = finalRun;
            var runs = Enumerable.Range( 1, JourneyHistoryWeeks ).Select( w => lastRun.AddDays( -7 * ( JourneyHistoryWeeks + 1 - w ) ) ).ToList();
            var predicted = new Dictionary<int, int>();

            foreach ( var (givingId, personIds) in people )
            {
                var dates = gifts.TryGetValue( givingId, out var unitGifts ) ? unitGifts.Select( g => g.When ).ToList() : new List<DateTime>();
                int? current = null;
                int? previous = null;
                DateTime currentSince = default, firstRun = default, previousSince = default;

                foreach ( var run in runs )
                {
                    var stage = ( int ) GetJourneyStage( dates, run );
                    if ( stage == current )
                    {
                        continue;
                    }

                    if ( current.HasValue )
                    {
                        previousSince = previous.HasValue ? previousSince : run;
                        previous = current;
                    }
                    else
                    {
                        firstRun = run;
                    }

                    current = stage;
                    currentSince = run;
                }

                add( personIds, CurrentStageGuid, current.ToString(), firstRun, currentSince );
                add( personIds, StageChangeDateGuid, SqlIsoValue( currentSince ), firstRun, currentSince );
                if ( previous.HasValue )
                {
                    add( personIds, PreviousStageGuid, previous.ToString(), previousSince, currentSince );
                }

                var finalStage = ( int ) GetJourneyStage( dates, finalRun );
                if ( finalStage != current )
                {
                    foreach ( var personId in personIds )
                    {
                        predicted[personId] = finalStage;
                    }
                }
            }

            return predicted;
        }

        /// <summary>
        /// Runs Rock's bins and percentiles procedure as of the job's last run.
        /// </summary>
        private void RunBinsAndPercentiles()
        {
            using var command = ProcedureCommand( "EXEC [dbo].[spGivingAutomation_UpdateGivingBinsAndPercentiles] @CurrentRockDateTime, @TransactionTypeIds, @FinancialAccountIds", RunAt );
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Runs Rock's journey stage procedure for the last Tuesday, then compares the changes it made to this
        /// dataset's people with the replay's prediction. Returns how many people differ.
        /// </summary>
        private int RunFinalJourneyStages( DateTime finalRun, Dictionary<int, int> predicted )
        {
            using ( var command = ProcedureCommand( "EXEC [dbo].[spGivingAutomation_UpdateGivingJourneyStages] @CurrentRockDateTime, @TransactionTypeIds, @FinancialAccountIds", finalRun ) )
            {
                command.ExecuteScalar();
            }

            var actual = _db.Query( @"
SELECT [av].[EntityId], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
WHERE [av].[AttributeId] = @AttributeId AND [av].[ModifiedDateTime] = @FinalRun AND [p].[ForeignKey] = @ForeignKey",
                P( "@AttributeId", _attributeIds[CurrentStageGuid] ), P( "@FinalRun", finalRun ), P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["EntityId"], r => int.Parse( ( string ) r["Value"] ) );

            return actual.Count( a => !predicted.TryGetValue( a.Key, out var stage ) || stage != a.Value )
                + predicted.Count( p => !actual.ContainsKey( p.Key ) );
        }

        /// <summary>
        /// A command for one of Rock's giving procedures, with Rock's default filters: contributions, and every
        /// tax-deductible account (GetGivingAutomationFilterIds with no accounts chosen).
        /// </summary>
        private SqlCommand ProcedureCommand( string sql, DateTime now )
        {
            var transactionTypes = TableFactory.Create( "Id:int" );
            transactionTypes.Rows.Add( _db.RequireIdByGuid( "DefinedValue", ContributionGuid ) );
            var accounts = TableFactory.Create( "Id:int" );
            foreach ( var row in _db.Query( "SELECT [Id] FROM [FinancialAccount] WHERE [IsTaxDeductible] = 1" ) )
            {
                accounts.Rows.Add( ( int ) row["Id"] );
            }

            var command = _db.CreateCommand( sql );
            command.Parameters.Add( P( "@CurrentRockDateTime", now ) );
            command.Parameters.Add( new SqlParameter( "@TransactionTypeIds", SqlDbType.Structured ) { TypeName = "dbo.IdList", Value = transactionTypes } );
            command.Parameters.Add( new SqlParameter( "@FinancialAccountIds", SqlDbType.Structured ) { TypeName = "dbo.IdList", Value = accounts } );
            return command;
        }

        /// <summary>
        /// Marks the attribute values Rock's procedures wrote for this dataset's people, so they are removed with the dataset.
        /// </summary>
        private void TagProcedureRows()
        {
            _db.Execute( $@"
UPDATE [av] SET [av].[ForeignKey] = @ForeignKey
FROM [AttributeValue] AS [av]
INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
WHERE [p].[ForeignKey] = @ForeignKey AND [av].[ForeignKey] IS NULL
    AND [av].[AttributeId] IN ( SELECT [Id] FROM [Attribute] WHERE [Guid] IN ( '7FBB63CC-F4FC-4F7E-A8C5-44DC3D0F0720', 'D03ACAB8-EB0C-4835-A04C-4C357014D935', '{CurrentStageGuid}', '{PreviousStageGuid}', '{StageChangeDateGuid}' ) )",
                P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Records the job's last run in Rock's giving automation settings, so Rock's next run only picks up
        /// gifts made after it (instead of reclassifying everyone from scratch).
        /// </summary>
        private void SaveLastRunDateTime( DateTime now )
        {
            var existing = _db.Scalar( "SELECT [DefaultValue] FROM [Attribute] WHERE [Key] = @Key AND [EntityTypeQualifierColumn] = 'SystemSetting'", P( "@Key", SettingsKey ) ) as string;
            var settings = ( string.IsNullOrWhiteSpace( existing ) ? null : JsonNode.Parse( existing ) as JsonObject ) ?? new JsonObject();
            if ( settings["GivingClassificationSettings"] is not JsonObject classification )
            {
                classification = new JsonObject();
                settings["GivingClassificationSettings"] = classification;
            }

            classification["LastRunDateTime"] = RunAt.ToString( "yyyy-MM-dd'T'HH:mm:ss" );
            var json = settings.ToJsonString();

            if ( existing != null )
            {
                _db.Execute( "UPDATE [Attribute] SET [DefaultValue] = @Value, [ModifiedDateTime] = @Now WHERE [Key] = @Key AND [EntityTypeQualifierColumn] = 'SystemSetting'",
                    P( "@Value", json ), P( "@Now", now ), P( "@Key", SettingsKey ) );
                return;
            }

            _db.Execute( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( 0, ( SELECT [Id] FROM [FieldType] WHERE [Guid] = '9C204CD0-1233-41C5-818A-C5DA439445AA' ), 'SystemSetting', '', @Key, 'Core Giving Automation Configuration', 0, 0, @Value, 0, 0, NEWID(), @Now, @Now, @ForeignKey )",
                P( "@Key", SettingsKey ), P( "@Value", json ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }
    }
}
