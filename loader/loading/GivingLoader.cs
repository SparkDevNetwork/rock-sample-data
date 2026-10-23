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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        10/15/26 - CLAUDE

        Each giving pattern keeps its own amounts, funds, rhythm, and growth. The profile
        decides the channel (scheduled online, one-time online, or onsite check or cash),
        the weekday or day of month, and seasonality. Onsite gifts only happen on Sundays
        the family attended. A pattern's "special gift percent" is the chance of a year-end
        gift each December, which produces the December spike real churches see.

        Reason: Giving must look like real giving and agree with attendance.
    */

    /// <summary>
    /// Generates batches, transactions, transaction details, payment details, and scheduled transactions.
    /// </summary>
    internal sealed class GivingLoader
    {
        private static readonly Guid ContributionGuid = new Guid( "2D607262-52D6-4724-910D-5C6E8FB89ACC" );
        private static readonly Guid WebsiteSourceGuid = new Guid( "7D705CE7-7B11-4342-A58E-53617C5B4E69" );
        private static readonly Guid OnsiteSourceGuid = new Guid( "BE7ECF50-52BC-4774-808D-574BA842DB98" );
        private static readonly Guid WeeklyFrequencyGuid = new Guid( "35711E44-131B-4534-B0B2-F0A749292362" );
        private static readonly Guid MonthlyFrequencyGuid = new Guid( "1400753C-A0F9-4A45-8A1D-81C98450BD1F" );
        private static readonly Guid BiWeeklyFrequencyGuid = new Guid( "72990023-0D43-4554-8D32-28461CAB8920" );
        private static readonly Guid QuarterlyFrequencyGuid = new Guid( "BF08EA03-C52A-4364-B142-12EBCA7CA14A" );

        /// <summary>
        /// Each persona's cadence name in the profile's giving.cadenceMix.
        /// </summary>
        public static readonly Dictionary<string, string> CadenceNames = new Dictionary<string, string>
        {
            ["one-time"] = "One time",
            ["occasional"] = "Occasional (2-3)",
            ["weekly"] = "Weekly",
            ["bi-weekly"] = "Bi-weekly or twice monthly",
            ["monthly"] = "Monthly",
            ["quarterly"] = "Quarterly",
            ["irregular"] = "Irregular"
        };

        private static readonly Dictionary<string, Guid> CurrencyGuids = new Dictionary<string, Guid>
        {
            ["Cash"] = new Guid( "F3ADC889-1EE8-4EB6-B3FD-8C10F3C8AF93" ),
            ["Check"] = new Guid( "8B086A19-405A-451F-8D44-174E92D6B402" ),
            ["Credit Card"] = new Guid( "928A2E04-C77B-4282-888F-EC549CEE026A" ),
            ["ACH"] = new Guid( "DABEE8FD-AEDF-43E1-8547-4C97FA14D9B6" ),
            ["Apple Pay"] = new Guid( "D42C4DF7-1AE9-4DDE-ADA2-774B866B798C" ),
            ["Non-Cash Asset"] = new Guid( "7950FF66-80EE-E8AB-4A77-4A13EDEB7513" )
        };

        private static readonly string[] Weekdays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        /// <summary>
        /// How many weeks back the open batch for transaction matching can be, counting from the newest Sunday with checks.
        /// </summary>
        private const int OpenBatchWeeks = 6;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly ProfileTargets _profile;
        private readonly SundayCalendar _calendar;
        private readonly AnchorDates _dates;
        private readonly ConfigurationLoader _configuration;
        private readonly Dictionary<int, HashSet<DateTime>> _familySundays;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly Dictionary<(string Fund, string Campus), int> _accountIds = new Dictionary<(string, string), int>();
        private readonly Dictionary<string, int> _definedValueIds = new Dictionary<string, int>();
        private readonly Dictionary<int, Wallet> _wallets = new Dictionary<int, Wallet>();

        public GivingLoader( RockDatabase db, SampleDataSource source, ProfileTargets profile, SundayCalendar calendar, AnchorDates dates, ConfigurationLoader configuration, Dictionary<int, HashSet<DateTime>> familySundays, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _profile = profile;
            _calendar = calendar;
            _dates = dates;
            _configuration = configuration;
            _familySundays = familySundays;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// One planned gift before it becomes rows. Units is how many of the pattern's usual gifts it stands for
        /// (two for a catch-up check after a missed Sunday), or 0 for a gift outside the pattern's rhythm.
        /// </summary>
        private sealed record Gift( DateTime When, bool Onsite, string Currency, int? ScheduleIndex, Dictionary<int, decimal> Amounts, decimal Units );

        /// <summary>
        /// What each giving pattern gives to the building campaign, from the pledge plan. Null when the package
        /// has no campaign.
        /// </summary>
        public IReadOnlyDictionary<CastGiving, CampaignGiving> Campaign { get; set; }

        /// <summary>
        /// Every check gift, with what its scanned image needs. Filled by <see cref="Load"/>.
        /// </summary>
        public List<CheckGift> CheckGifts { get; } = new List<CheckGift>();

        /// <summary>
        /// Every gift Rock's Giving Automation job would count, by giving id and in date order. Filled by
        /// <see cref="Load"/>, so giving analytics does not read the gifts back from the database.
        /// </summary>
        public Dictionary<string, List<RockGift>> QualifyingGifts { get; private set; }

        /// <summary>
        /// Rock's PasswordKey, used to fill the check scanner's matching fields. When null, those fields stay empty.
        /// </summary>
        public RockKeys Keys { get; set; }

        /// <summary>
        /// The planned rows, in foreign key order, which <see cref="Write"/> writes.
        /// </summary>
        private List<(string Table, DataTable Rows)> _planned;

        /// <summary>
        /// The line that names the open check batch, reported when the gifts are written.
        /// </summary>
        private string _openBatchLine;

        /// <summary>
        /// Plans every gift and fills <see cref="CheckGifts"/> and <see cref="QualifyingGifts"/>. Prayer reads
        /// the plan even when giving is not written, so planning never depends on the parts chosen.
        /// </summary>
        public void Plan( List<LoadedHousehold> households )
        {
            var now = DateTime.Now;
            var contributionId = DefinedValueId( ContributionGuid );
            var gatewayId = _db.Scalar( "SELECT TOP 1 [Id] FROM [FinancialGateway] WHERE [IsActive] = 1 ORDER BY [Id]" );

            var batches = TableFactory.Create( "Id:int", "Name:string", "BatchStartDateTime:date", "BatchEndDateTime:date", "Status:int", "CampusId:int", "ControlAmount:decimal", "ControlItemCount:int", "IsAutomated:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var paymentDetails = TableFactory.Create( "Id:int", "CurrencyTypeValueId:int", "CreditCardTypeValueId:int", "AccountNumberMasked:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var schedules = TableFactory.Create( "Id:int", "TransactionFrequencyValueId:int", "StartDate:date", "EndDate:date", "NextPaymentDate:date", "IsActive:bool", "AuthorizedPersonAliasId:int", "FinancialGatewayId:int", "FinancialPaymentDetailId:int", "SourceTypeValueId:int", "TransactionTypeValueId:int", "GatewayScheduleId:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var scheduleDetails = TableFactory.Create( "Id:int", "ScheduledTransactionId:int", "AccountId:int", "Amount:decimal", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var transactions = TableFactory.Create( "Id:int", "BatchId:int", "TransactionDateTime:date", "TransactionCode:string", "TransactionTypeValueId:int", "SourceTypeValueId:int", "ScheduledTransactionId:int", "AuthorizedPersonAliasId:int", "FinancialGatewayId:int", "FinancialPaymentDetailId:int", "IsSettled:bool", "SettledDate:date", "ShowAsAnonymous:bool", "SundayDate:date", "TransactionDateKey:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CheckMicrHash:string", "MICRStatus:int", "CheckMicrEncrypted:string", "CheckMicrParts:string" );
            var details = TableFactory.Create( "Id:int", "TransactionId:int", "AccountId:int", "Amount:decimal", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var nextBatchId = NextId( "FinancialBatch" );
            var nextPaymentId = NextId( "FinancialPaymentDetail" );
            var nextScheduleId = NextId( "FinancialScheduledTransaction" );
            var nextScheduleDetailId = NextId( "FinancialScheduledTransactionDetail" );
            var nextTransactionId = NextId( "FinancialTransaction" );
            var nextDetailId = NextId( "FinancialTransactionDetail" );
            var batchIds = new Dictionary<(DateTime Date, int Campus, string Kind), int>();
            var batchTotals = new Dictionary<int, (decimal Amount, int Count)>();
            var taxDeductibleAccountIds = _db.Query( "SELECT [Id] FROM [FinancialAccount] WHERE [IsTaxDeductible] = 1" ).Select( r => ( int ) r["Id"] ).ToHashSet();
            var qualifying = new List<RockGift>();

            foreach ( var household in households )
            {
                foreach ( var pattern in household.Cast.Giving )
                {
                    var giver = household.Members.First( m => m.Cast.Key == pattern.Person );
                    var wallet = WalletFor( giver );
                    var random = new SeededRandom( $"{_seed}:giving:{household.Cast.Guid}:{pattern.Person}:{pattern.Persona}:{pattern.Started?.YearsAgo}-{pattern.Started?.Month}-{pattern.Started?.Day}" );
                    var campusKey = household.CampusKey;
                    var plan = _configuration.CampusPlans[campusKey];
                    var attendRate = _familySundays.TryGetValue( household.FamilyId, out var familySundays ) ? familySundays.Count / ( double ) Math.Max( 1, _calendar.Sundays.Count ) : 0;
                    var age = giver.BirthDate.HasValue ? _dates.AgeOn( giver.BirthDate.Value ) : ( int? ) null;
                    var channel = pattern.Currency != null ? ChosenChannel( pattern.Currency, attendRate > 0 && giver.Cast.RecordType != "business", random ) : PickChannel( pattern, age, attendRate, random );
                    var started = _dates.Resolve( pattern.Started ) ?? _calendar.HistoryStart;
                    var ended = _dates.Resolve( pattern.Ended );
                    var gifts = PlanGifts( pattern, channel, household, started, ended, random );

                    // A scheduled pattern gets one Rock scheduled transaction that its gifts point to.
                    int? scheduleId = null;
                    if ( channel.Scheduled && gifts.Count > 0 )
                    {
                        scheduleId = nextScheduleId++;
                        var paymentId = nextPaymentId++;
                        paymentDetails.Rows.Add( paymentId, DefinedValueId( CurrencyGuids[channel.Currency] ), CardBrand( channel.Currency, wallet ), wallet.Masked( channel.Currency ) ?? ( object ) DBNull.Value, Guid.NewGuid(), _foreignKey, gifts[0].When, now );
                        var isActive = ended == null || ended > _calendar.HistoryEnd;
                        var lastGift = gifts[gifts.Count - 1];
                        var (frequency, next) = pattern.Persona switch
                        {
                            "weekly" => (WeeklyFrequencyGuid, lastGift.When.Date.AddDays( 7 )),
                            "bi-weekly" => (BiWeeklyFrequencyGuid, lastGift.When.Date.AddDays( 14 )),
                            "quarterly" => (QuarterlyFrequencyGuid, lastGift.When.Date.AddMonths( 3 )),
                            _ => (MonthlyFrequencyGuid, lastGift.When.Date.AddMonths( 1 ))
                        };
                        schedules.Rows.Add( scheduleId, DefinedValueId( frequency ), gifts[0].When.Date, isActive ? DBNull.Value : ( object ) lastGift.When.Date, isActive ? ( object ) next : DBNull.Value, isActive,
                            giver.AliasId, gatewayId ?? DBNull.Value, paymentId, DefinedValueId( WebsiteSourceGuid ), contributionId, $"SCH{scheduleId:D7}", Guid.NewGuid(), _foreignKey, gifts[0].When, now );
                        foreach ( var amount in lastGift.Amounts )
                        {
                            scheduleDetails.Rows.Add( nextScheduleDetailId++, scheduleId, amount.Key, amount.Value, Guid.NewGuid(), _foreignKey, gifts[0].When, now );
                        }
                    }

                    foreach ( var gift in gifts )
                    {
                        var date = gift.When.Date;

                        // The Sunday offering is two batches: its checks go through the check scanner in a batch of their own,
                        // and its cash is counted and entered by hand. Checks that do not arrive in the Sunday offering come by
                        // mail and are entered during the week. Gifts of stock or property are entered by hand in a batch of their own.
                        var isMailed = !gift.Onsite && gift.Currency is "Check" or "Cash";
                        var isNonCash = gift.Currency == "Non-Cash Asset";
                        var isManual = gift.Onsite || isMailed || isNonCash;
                        var batchKind = gift.Onsite ? ( gift.Currency == "Check" ? "onsite-checks" : "onsite-cash" ) : isMailed ? "mail" : isNonCash ? "noncash" : "online";
                        var batchKey = (date, plan.CampusId, batchKind);
                        if ( !batchIds.TryGetValue( batchKey, out var batchId ) )
                        {
                            batchId = nextBatchId++;
                            batchIds[batchKey] = batchId;
                            var name = batchKind switch
                            {
                                "onsite-checks" => $"Sunday Offering Checks {date:MM/dd/yyyy}",
                                "onsite-cash" => $"Sunday Offering Cash {date:MM/dd/yyyy}",
                                "mail" => $"Mailed Checks {date:MM/dd/yyyy}",
                                "noncash" => $"Non-Cash Gifts {date:MM/dd/yyyy}",
                                _ => $"Online Giving {date:MM/dd/yyyy}"
                            };
                            batches.Rows.Add( batchId, name, date, date.AddDays( 1 ), 2, plan.CampusId, 0m, 0, !isManual, Guid.NewGuid(), _foreignKey, date, date );
                        }

                        var total = gift.Amounts.Values.Sum();
                        batchTotals[batchId] = batchTotals.TryGetValue( batchId, out var t ) ? (t.Amount + total, t.Count + 1) : (total, 1);

                        var paymentId = nextPaymentId++;
                        paymentDetails.Rows.Add( paymentId, DefinedValueId( CurrencyGuids[gift.Currency] ), CardBrand( gift.Currency, wallet ), wallet.Masked( gift.Currency ) ?? ( object ) DBNull.Value, Guid.NewGuid(), _foreignKey, gift.When, gift.When );

                        var transactionId = nextTransactionId++;
                        // Check numbers are filled in after every gift is planned, in date order per writer.
                        var code = isManual ? ( object ) DBNull.Value : $"T{random.Between( 100000000, 999999999 )}";
                        var transactionRow = transactions.Rows.Add( transactionId, batchId, gift.When, code, contributionId, DefinedValueId( isManual ? OnsiteSourceGuid : WebsiteSourceGuid ),
                            gift.ScheduleIndex.HasValue ? ( object ) scheduleId : DBNull.Value, giver.AliasId, isManual ? DBNull.Value : gatewayId ?? DBNull.Value, paymentId,
                            !isManual, isManual ? DBNull.Value : ( object ) date.AddDays( 1 ), false, SundayCalendar.SundayOnOrAfter( date ), int.Parse( date.ToString( "yyyyMMdd" ) ),
                            Guid.NewGuid(), _foreignKey, gift.When, gift.When );

                        foreach ( var amount in gift.Amounts )
                        {
                            details.Rows.Add( nextDetailId++, transactionId, amount.Key, amount.Value, Guid.NewGuid(), _foreignKey, gift.When, gift.When );
                        }

                        // The same rule as Rock's GetGivingAutomationSourceTransactionQuery: a contribution with a line in a
                        // tax-deductible account and a positive line. Amounts get the database's two decimal places, so
                        // values Rock prints from them (median, IQR) read the same as when read back from the database.
                        if ( gift.Amounts.Keys.Any( taxDeductibleAccountIds.Contains ) && gift.Amounts.Values.Any( a => a > 0 ) )
                        {
                            qualifying.Add( new RockGift( transactionId, giver.AliasId, giver.GivingId, giver.CampusId, gift.When,
                                DefinedValueId( isManual ? OnsiteSourceGuid : WebsiteSourceGuid ), DefinedValueId( CurrencyGuids[gift.Currency] ), gift.ScheduleIndex.HasValue,
                                gift.Amounts.Where( a => a.Value != 0 ).OrderBy( a => a.Key ).Select( a => (a.Key, a.Value + 0.00m) ).ToList() ) );
                        }

                        if ( gift.Currency == "Check" )
                        {
                            CheckGifts.Add( new CheckGift( transactionId, transactionRow, giver, household, wallet, gift.When, total, gift.Amounts.Keys.Select( FundName ).ToList(), pattern.Signer ) );
                        }
                    }
                }
            }

            // One batch of checks waits to be matched; Rock's giving automation does not count its gifts yet.
            var unmatched = LeaveRecentCheckBatchUnmatched( batches, batchIds );
            qualifying.RemoveAll( g => unmatched.Contains( g.Id ) );

            // By giving unit and in date order, the way giving analytics reads them from the database.
            QualifyingGifts = qualifying
                .GroupBy( g => g.GivingId )
                .ToDictionary( g => g.Key, g => g.OrderBy( x => x.When ).ThenBy( x => x.Id ).ToList() );

            AssignCheckNumbers();
            var bankAccounts = CheckWriterBankAccounts( now );

            foreach ( DataRow row in batches.Rows )
            {
                var totals = batchTotals[( int ) row["Id"]];
                row["ControlAmount"] = totals.Amount;
                row["ControlItemCount"] = totals.Count;
            }

            _planned = new List<(string, DataTable)>
            {
                ("FinancialBatch", batches),
                ("FinancialPaymentDetail", paymentDetails),
                ("FinancialScheduledTransaction", schedules),
                ("FinancialScheduledTransactionDetail", scheduleDetails),
                ("FinancialTransaction", transactions),
                ("FinancialTransactionDetail", details)
            };
            if ( bankAccounts != null )
            {
                _planned.Add( ("FinancialPersonBankAccount", bankAccounts) );
            }
        }

        /// <summary>
        /// Writes the gifts <see cref="Plan"/> planned, in foreign key order.
        /// </summary>
        public void Write()
        {
            if ( _openBatchLine != null )
            {
                _db.Log.Write( _openBatchLine );
            }

            foreach ( var (table, rows) in _planned )
            {
                _db.BulkInsert( table, rows, keepIdentity: true );
            }

            var tables = _planned.ToDictionary( p => p.Table, p => p.Rows );
            var dollars = tables["FinancialTransactionDetail"].AsEnumerable().Sum( r => ( decimal ) r["Amount"] );
            _db.Log.Write( $"  Gifts: {tables["FinancialTransaction"].Rows.Count:N0} (${dollars:N0}) in {tables["FinancialBatch"].Rows.Count:N0} batches, scheduled gifts: {tables["FinancialScheduledTransaction"].Rows.Count:N0}" );
        }

        /*
            10/21/26 - CLAUDE

            Rock Cleanup sets each campus's tithe metric every night: the census median family income for the
            postal code of each family that gave tax-deductible gifts in the last 365 days, averaged per campus
            and multiplied by 10%. Rock fills a temp table and walks the campuses with a cursor; this is the same
            query as one UPDATE. Rock's temp table has INT columns, so the income and the giving amount are cut
            to whole numbers after its filters and before its math, and the average is integer division, as in
            Rock. The window ends on the anchor date, which is the install day Rock Cleanup would see.

            Reason: Mirrors RockCleanup.UpdateCampusTitheMetric (see validation/rock-lock.json).
        */

        /// <summary>
        /// Sets each campus's tithe metric as Rock Cleanup would, and returns how many campuses got one.
        /// </summary>
        public int UpdateCampusTitheMetric()
        {
            return _db.Execute( @"
WITH [Families] AS (
    SELECT [p].[PrimaryCampusId]
        , [p].[PrimaryFamilyId]
        , SUM( [ftd].[Amount] ) AS [GivingAmount]
        , ( SELECT TOP 1 LEFT( [l].[PostalCode], 5 )
            FROM [Location] AS [l]
            INNER JOIN [GroupLocation] AS [gl] ON [gl].[LocationId] = [l].[Id] AND [gl].[GroupId] = [p].[PrimaryFamilyId] AND [gl].[IsMappedLocation] = 1 ) AS [PostalCode]
    FROM [Person] AS [p]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [p].[Id]
    INNER JOIN [FinancialTransaction] AS [ft] ON [ft].[AuthorizedPersonAliasId] = [pa].[Id]
    INNER JOIN [FinancialTransactionDetail] AS [ftd] ON [ftd].[TransactionId] = [ft].[Id]
    INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [ftd].[AccountId]
    INNER JOIN [AnalyticsSourceDate] AS [asd] ON [asd].[DateKey] = [ft].[TransactionDateKey]
    WHERE [fa].[IsTaxDeductible] = 1
        AND [asd].[DateKey] >= @StartDateKey AND [asd].[DateKey] <= @EndDateKey
        AND [p].[RecordTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' )
    GROUP BY [p].[PrimaryCampusId], [p].[PrimaryFamilyId]
), [WithIncome] AS (
    SELECT [f].[PrimaryCampusId]
        , [f].[GivingAmount]
        , [f].[PostalCode]
        , ( SELECT [aspc].[FamiliesMedianIncome] FROM [AnalyticsSourcePostalCode] AS [aspc] WHERE [aspc].[PostalCode] = [f].[PostalCode] ) AS [FamiliesMedianTithe]
    FROM [Families] AS [f]
), [GivingFamilies] AS (
    SELECT [PrimaryCampusId]
        , CAST( [FamiliesMedianTithe] AS INT ) AS [FamiliesMedianTithe]
        , CAST( [GivingAmount] AS INT ) AS [GivingAmount]
    FROM [WithIncome]
    WHERE [PostalCode] IS NOT NULL AND [PostalCode] != '' AND [FamiliesMedianTithe] IS NOT NULL AND [FamiliesMedianTithe] > 0
        AND [PrimaryCampusId] IS NOT NULL
)
UPDATE [c]
SET [c].[TitheMetric] = ( [g].[TotalMedianIncome] / [g].[HasGivenCount] ) * 0.1
FROM [Campus] AS [c]
INNER JOIN (
    SELECT [PrimaryCampusId]
        , SUM( CASE WHEN [GivingAmount] >= 0 THEN 1 ELSE 0 END ) AS [HasGivenCount]
        , SUM( [FamiliesMedianTithe] ) AS [TotalMedianIncome]
    FROM [GivingFamilies]
    GROUP BY [PrimaryCampusId]
) AS [g] ON [g].[PrimaryCampusId] = [c].[Id];",
                RockDatabase.P( "@StartDateKey", int.Parse( _dates.Anchor.AddDays( -365 ).ToString( "yyyyMMdd" ) ) ),
                RockDatabase.P( "@EndDateKey", int.Parse( _dates.Anchor.ToString( "yyyyMMdd" ) ) ) );
        }

        /// <summary>
        /// How a giving pattern gives: scheduled online, one-time online, or onsite.
        /// </summary>
        private sealed record Channel( bool Scheduled, bool Onsite, string Currency, string Weekday, int DayOfMonth );

        /// <summary>
        /// The channel for a pattern whose Cast file names its payment method. Checks and cash go in the
        /// Sunday offering when the giver comes on Sundays; otherwise checks arrive by mail. ACH and cards are scheduled.
        /// </summary>
        private Channel ChosenChannel( string currency, bool comesOnSunday, SeededRandom random )
        {
            var dayOfMonth = int.Parse( random.Pick( ScheduledDayOfMonthWeights() ) );
            return currency switch
            {
                "ach" => new Channel( true, false, "ACH", "Mon", dayOfMonth ),
                "card" => new Channel( true, false, "Credit Card", "Mon", dayOfMonth ),
                "cash" when comesOnSunday => new Channel( false, true, "Cash", "Sun", dayOfMonth ),
                _ => new Channel( false, comesOnSunday, "Check", "Sun", dayOfMonth )
            };
        }

        /// <summary>
        /// Picks a pattern's channel from the profile: the scheduled share for its cadence, then the
        /// payment method mix, weighted by the giver's age and how much they give.
        /// </summary>
        private Channel PickChannel( CastGiving pattern, int? age, double attendRate, SeededRandom random )
        {
            // Rhythmic personas can be a Rock scheduled transaction. So can an irregular giver, whose
            // small monthly schedule sits under extra gifts given now and then.
            var persona = pattern.Persona;
            var canSchedule = persona is "weekly" or "bi-weekly" or "monthly" or "quarterly" or "irregular";
            var scheduledShare = canSchedule ? _profile.Number( $"giving.cadenceMix.{CadenceNames[persona]}.pctGiftsScheduled", 50 ) / 100 * ScheduledScale( persona ) : 0;
            var mix = PaymentMix();
            var weekday = random.Pick( WeeklyScheduleWeekdayWeights() );
            var dayOfMonth = int.Parse( random.Pick( MonthlyScheduleDayWeights() ) );
            var yearly = ( double ) ( pattern.Funds.Values.Sum() * GiftsPerMonth( persona ) * 12 );
            var sizeRatio = yearly / _profile.Number( "giving.unitAnnualTotalPercentiles.p50", 1500 );

            if ( random.Chance( scheduledShare ) )
            {
                // Scheduled givers lean to ACH, larger givers most of all (no card fees); one-time online
                // gifts carry most of the card share.
                var currency = random.Pick( new Dictionary<string, double> { ["ACH"] = mix.GetValueOrDefault( "ACH", 35 ) * AchScale * SizeWeight( sizeRatio, 0.6 ), ["Credit Card"] = mix.GetValueOrDefault( "Credit Card", 44 ) * 0.5 } );
                return new Channel( true, false, currency, weekday, dayOfMonth );
            }

            /*
                10/16/26 - CLAUDE

                Older givers write most checks, and their checks are larger. So a check is weighted by the
                giver's age and by the pattern's yearly total against the profile's median. A check writer
                who comes on Sundays puts it in the offering; one who rarely comes mails it. Younger givers
                lean to Apple Pay. Cash stays small and onsite, and leans to small gifts.

                Reason: Real check and Apple Pay shares follow age and gift size, not chance.
            */
            var checkWeight = mix.GetValueOrDefault( "Check", 9 ) * CheckAgeWeight( age ) * SizeWeight( sizeRatio, 0.6 ) * CheckScale;
            var comesOnSunday = attendRate >= MailedCheckAttendRate;
            var cashWeight = comesOnSunday ? mix.GetValueOrDefault( "Cash", 4 ) * ( yearly < 1000 ? 1.5 : 0.5 ) * CashScale : 0;
            var applePayWeight = mix.GetValueOrDefault( "Apple Pay", 8 ) * ApplePayAgeWeight( age ) * ApplePayScale / SizeWeight( sizeRatio, 0.4 );
            var cardWeight = mix.GetValueOrDefault( "Credit Card", 44 ) * 0.5;

            // A large gift given online by hand usually comes from a bank account, which has no card fee.
            var bankWeight = mix.GetValueOrDefault( "ACH", 35 ) * OneTimeAchScale * SizeWeight( sizeRatio, 1.0 );
            var pick = random.Pick( new Dictionary<string, double> { ["Check"] = checkWeight, ["Cash"] = cashWeight, ["Apple Pay"] = applePayWeight, ["Credit Card"] = cardWeight, ["ACH"] = bankWeight } );

            return pick switch
            {
                "Check" => new Channel( false, comesOnSunday, "Check", "Sun", dayOfMonth ),
                "Cash" => new Channel( false, true, "Cash", "Sun", dayOfMonth ),
                _ => new Channel( false, false, pick, weekday, dayOfMonth )
            };
        }

        /// <summary>
        /// A weight that grows with the pattern's yearly total against the median giver. A bigger
        /// exponent leans harder toward large givers.
        /// </summary>
        private static double SizeWeight( double sizeRatio, double exponent )
        {
            return Math.Min( Math.Max( Math.Pow( Math.Max( 0.01, sizeRatio ), exponent ), 0.25 ), 4 );
        }

        /// <summary>
        /// How likely a giver of this age is to write checks, against the average giver.
        /// </summary>
        private static double CheckAgeWeight( int? age )
        {
            return age switch
            {
                null => 1,
                < 35 => 0.3,
                < 50 => 0.7,
                < 65 => 1.5,
                _ => 3
            };
        }

        /// <summary>
        /// How likely a giver of this age is to give with Apple Pay, against the average giver.
        /// </summary>
        private static double ApplePayAgeWeight( int? age )
        {
            return age switch
            {
                null => 1,
                < 35 => 2,
                < 50 => 1.2,
                < 65 => 0.6,
                _ => 0.25
            };
        }

        // Tuning knobs for the payment mix, found by running the validation loop.
        /// <summary>
        /// Scales the profile's scheduled share for a cadence. The profile measures it on finished data,
        /// where some hand-given patterns drift into other cadences; these bring the measured share back to the profile.
        /// </summary>
        private static double ScheduledScale( string persona )
        {
            return persona switch
            {
                "weekly" => 0.92,
                "monthly" => 0.88,
                "irregular" => 1.5,
                _ => 1
            };
        }

        private const double AchScale = 1.0;
        private const double OneTimeAchScale = 0.06;
        private const double CheckScale = 0.8;
        private const double CashScale = 6.0;
        private const double ApplePayScale = 0.5;

        /// <summary>
        /// Below this share of Sundays attended, a check writer mails their checks.
        /// </summary>
        private const double MailedCheckAttendRate = 0.2;

        /// <summary>
        /// Lays out a pattern's gifts across the history window.
        /// </summary>
        private List<Gift> PlanGifts( CastGiving pattern, Channel channel, LoadedHousehold household, DateTime started, DateTime? ended, SeededRandom random )
        {
            var gifts = new List<Gift>();
            var first = started > _calendar.HistoryStart ? started : _calendar.HistoryStart;
            var last = ended.HasValue && ended < _calendar.HistoryEnd ? ended.Value : _calendar.HistoryEnd;
            var percent = ( pattern.PercentOfPeriodsGiven ?? 90 ) / 100.0;
            var attended = _familySundays.TryGetValue( household.FamilyId, out var sundays ) ? sundays : new HashSet<DateTime>();
            var plan = _configuration.CampusPlans[household.CampusKey];

            /*
                10/16/26 - CLAUDE

                How amounts vary, by kind of giver:
                - Irregular and occasional givers rarely give the same amount twice (a wide, long-tailed spread).
                - Hand-given rhythmic gifts (onsite, mailed, or typed in online) are usually the usual amount, but
                  each giver strays some share of the time: a smaller week, a bigger one, a double.
                - Scheduled gifts repeat exactly, except that now and then the giver raises or lowers the schedule.
                A few givers give exact amounts ($37, $112) instead of round ones.

                Reason: The profile's amount variation and usual-amount share per cadence.
            */
            var isLoose = pattern.Persona is "irregular" or "occasional";
            var strayShare = random.Between( StrayShareMin, StrayShareMax ) * ( pattern.Persona == "quarterly" ? 1.15 : 1 );
            var catchesUp = random.Chance( CatchUpShare );
            var usualWeekday = random.Pick( WeekdayWeights( "oneTime" ) );
            var exactAmounts = random.Chance( ExactAmountShare );
            var scheduleChanges = channel.Scheduled ? ScheduleChanges( first, last, random ) : new List<(DateTime When, decimal Factor)>();
            var centsShare = _profile.Number( "giving.giftAmounts.pctWithCents", 5 ) / 100;

            void Add( DateTime when, bool onsite, string currency, int? scheduleIndex, decimal multiplier = 1, string onlyFund = null )
            {
                if ( when < first || when > last )
                {
                    return;
                }

                var units = onlyFund == null ? multiplier : 0;
                if ( onlyFund == null )
                {
                    if ( isLoose && !scheduleIndex.HasValue )
                    {
                        multiplier *= random.Chance( LooseUsualShare ) ? 1 : ( decimal ) random.LogNormal( LooseAmountSpread );
                    }
                    else if ( scheduleIndex.HasValue )
                    {
                        multiplier *= scheduleChanges.Where( c => c.When <= when ).Select( c => c.Factor ).DefaultIfEmpty( 1 ).Last();
                    }
                    else if ( random.Chance( strayShare ) )
                    {
                        multiplier *= decimal.Parse( random.Pick( StrayFactors ), System.Globalization.CultureInfo.InvariantCulture );
                    }
                }

                var amounts = new Dictionary<int, decimal>();
                foreach ( var fund in pattern.Funds.Where( f => onlyFund == null || f.Key == onlyFund ) )
                {
                    var amount = RoundGift( fund.Value * Growth( pattern, started, when ) * multiplier, exactAmounts );

                    // Some online givers cover the processing fee, which leaves cents on the gift.
                    if ( !onsite && random.Chance( centsShare ) )
                    {
                        amount = Math.Round( amount * 1.03m, 2 );
                    }

                    amounts[AccountId( fund.Key, household.CampusKey )] = amount;
                }

                gifts.Add( new Gift( when, onsite, currency, scheduleIndex, amounts, units ) );
            }

            // Where a gift that is not tied to a Sunday lands. A check writer who comes on Sundays brings it the
            // next Sunday the family is there (within two weeks), and otherwise mails it. A cash giver gives
            // it online instead. Everyone else gives it the way they always do.
            (DateTime When, bool Onsite, string Currency) OffSunday( DateTime when, string fallbackCurrency )
            {
                if ( channel.Currency == "Check" )
                {
                    var sunday = channel.Onsite ? attended.Where( s => s >= when.Date && s <= when.Date.AddDays( 14 ) ).OrderBy( s => s ).FirstOrDefault() : default;
                    return sunday != default ? (sunday.Add( plan.Services[0].Start ).AddMinutes( 30 ), true, "Check") : (when, false, "Check");
                }

                return (when, false, channel.Onsite ? fallbackCurrency : channel.Currency);
            }

            void AddOffSunday( DateTime when, string fallbackCurrency, decimal multiplier = 1, string onlyFund = null )
            {
                var placed = OffSunday( when, fallbackCurrency );
                Add( placed.When, placed.Onsite, placed.Currency, null, multiplier, onlyFund );
            }

            switch ( pattern.Persona )
            {
                case "weekly":
                case "bi-weekly":
                    var everyOtherWeek = pattern.Persona == "bi-weekly";
                    var weekNumber = 0;
                    var missed = 0;
                    foreach ( var sunday in _calendar.Sundays )
                    {
                        if ( everyOtherWeek && weekNumber++ % 2 == 1 )
                        {
                            continue;
                        }

                        var seasonal = Math.Min( 1.2, _profile.Number( $"giving.giftsMonthIndex.{sunday:MM}", 1 ) );
                        if ( channel.Onsite )
                        {
                            // A missed Sunday's gift often comes the next Sunday the family is there, in one larger check.
                            if ( !attended.Contains( sunday ) )
                            {
                                missed += sunday >= first ? 1 : 0;
                                continue;
                            }

                            if ( random.Chance( Math.Min( 1, percent * seasonal ) ) )
                            {
                                var covered = catchesUp && missed > 0 && random.Chance( 0.7 ) ? Math.Min( missed, 3 ) : 0;
                                Add( sunday.Add( plan.Services[0].Start ).AddMinutes( 30 ), true, channel.Currency, null, 1 + covered );
                            }

                            missed = 0;
                        }
                        else if ( channel.Scheduled )
                        {
                            if ( random.Chance( 0.98 ) )
                            {
                                Add( WeekdayInWeekEnding( sunday, channel.Weekday ).AddHours( 3 ), false, channel.Currency, 0 );
                            }
                        }
                        else if ( random.Chance( Math.Min( 1, percent * seasonal ) ) )
                        {
                            // People who give by hand each week usually do it on the same day.
                            var weekday = random.Chance( UsualWeekdayShare ) ? usualWeekday : random.Pick( WeekdayWeights( "oneTime" ) );
                            Add( WeekdayInWeekEnding( sunday, weekday ).AddHours( random.Between( 6, 22 ) ).AddMinutes( random.Between( 0, 59 ) ), false, channel.Currency, null );
                        }
                    }

                    break;

                case "monthly":
                    var missedMonth = false;
                    for ( var month = new DateTime( first.Year, first.Month, 1 ); month <= last; month = month.AddMonths( 1 ) )
                    {
                        if ( channel.Scheduled )
                        {
                            if ( random.Chance( 0.98 ) )
                            {
                                Add( month.AddDays( Math.Min( channel.DayOfMonth, DateTime.DaysInMonth( month.Year, month.Month ) ) - 1 ).AddHours( 3 ), false, channel.Currency, 0 );
                            }
                        }
                        else if ( random.Chance( percent ) )
                        {
                            if ( channel.Onsite )
                            {
                                // A month with no Sunday at church is often made up the next month.
                                var sunday = attended.Where( s => s.Year == month.Year && s.Month == month.Month ).OrderBy( s => s ).FirstOrDefault();
                                if ( sunday != default )
                                {
                                    Add( sunday.Add( plan.Services[0].Start ).AddMinutes( 30 ), true, channel.Currency, null, missedMonth && catchesUp ? 2 : 1 );
                                }

                                missedMonth = sunday == default;
                            }
                            else
                            {
                                // Monthly givers who give by hand (online or a mailed check) do it around the same day each month.
                                var day = random.Chance( UsualWeekdayShare ) ? Math.Min( Math.Max( channel.DayOfMonth + random.Between( -3, 3 ), 1 ), 28 ) : random.Between( 1, 28 );
                                Add( month.AddDays( day - 1 ).AddHours( random.Between( 6, 22 ) ), false, channel.Currency, null );
                            }
                        }
                    }

                    break;

                case "quarterly":
                    for ( var month = new DateTime( first.Year, ( ( first.Month - 1 ) / 3 * 3 ) + 1, 1 ); month <= last; month = month.AddMonths( 3 ) )
                    {
                        if ( channel.Scheduled )
                        {
                            Add( month.AddDays( Math.Min( channel.DayOfMonth, 28 ) - 1 ).AddHours( 3 ), false, channel.Currency, 0 );
                        }
                        else if ( random.Chance( percent ) )
                        {
                            AddOffSunday( month.AddDays( random.Between( 0, 27 ) ).AddHours( random.Between( 6, 22 ) ), "Credit Card" );
                        }
                    }

                    break;

                case "irregular":
                    // A scheduled irregular giver has a small monthly schedule, plus extra gifts online now and then.
                    if ( channel.Scheduled )
                    {
                        for ( var month = new DateTime( first.Year, first.Month, 1 ); month <= last; month = month.AddMonths( 1 ) )
                        {
                            Add( month.AddDays( Math.Min( channel.DayOfMonth, 28 ) - 1 ).AddHours( 3 ), false, channel.Currency, 0, IrregularScheduleAmount );
                        }
                    }

                    // Gives in about a third of weeks: onsite when the family attends, or online any day.
                    foreach ( var sunday in _calendar.Sundays )
                    {
                        if ( !random.Chance( 0.35 * percent / 0.9 * ( channel.Scheduled ? 0.5 : 1 ) ) )
                        {
                            continue;
                        }

                        if ( channel.Scheduled )
                        {
                            var weekday = random.Pick( WeekdayWeights( "oneTime" ) );
                            Add( WeekdayInWeekEnding( sunday, weekday ).AddHours( random.Between( 6, 22 ) ), false, "Credit Card", null );
                        }
                        else if ( channel.Onsite )
                        {
                            if ( attended.Contains( sunday ) )
                            {
                                Add( sunday.Add( plan.Services[0].Start ).AddMinutes( 30 ), true, channel.Currency, null );
                            }
                        }
                        else
                        {
                            var weekday = random.Pick( WeekdayWeights( "oneTime" ) );
                            Add( WeekdayInWeekEnding( sunday, weekday ).AddHours( random.Between( 6, 22 ) ).AddMinutes( random.Between( 0, 59 ) ), false, channel.Currency, null );
                        }
                    }

                    break;

                case "occasional":
                    // Two or three gifts a year on random days.
                    for ( var year = first.Year; year <= last.Year; year++ )
                    {
                        var count = random.Chance( 0.6 ) ? 2 : 3;
                        for ( var i = 0; i < count; i++ )
                        {
                            AddOffSunday( new DateTime( year, 1, 1 ).AddDays( random.Between( 0, 364 ) ).AddHours( random.Between( 6, 22 ) ), "Credit Card" );
                        }
                    }

                    break;

                case "year-end":
                    // One gift a year, in the last two weeks of December.
                    for ( var year = first.Year; year <= last.Year; year++ )
                    {
                        if ( random.Chance( percent ) )
                        {
                            var day = random.Chance( 0.4 ) ? 31 : random.Between( 17, 30 );
                            AddOffSunday( new DateTime( year, 12, day ).AddHours( random.Between( 8, 23 ) ), "Credit Card", ( decimal ) random.Between( 0.85, 1.2 ) );
                        }
                    }

                    break;

                default:
                    // One-time gifts land on their own date, online by card.
                    Add( started.AddHours( 10 ), false, "Credit Card", null );
                    break;
            }

            var generalFund = pattern.Funds.Keys.FirstOrDefault( k => k == "General Fund" ) ?? pattern.Funds.Keys.First();
            var yearly = pattern.Funds.Values.Sum() * GiftsPerMonth( pattern.Persona ) * 12;

            // Year-end gifts: a chance each December, mostly in the last week and on December 31.
            if ( pattern.SpecialGiftPercent > 0 && pattern.Persona is not ( "one-time" or "year-end" ) )
            {
                for ( var year = first.Year; year <= last.Year; year++ )
                {
                    if ( !random.Chance( pattern.SpecialGiftPercent.Value / 100.0 ) )
                    {
                        continue;
                    }

                    // Sized from the giver's normal month, so December lands near the profile's month index.
                    // Check writers write a year-end check; everyone else gives it online.
                    var day = random.Chance( 0.35 ) ? 31 : random.Between( 18, 30 );
                    var multiplier = ( decimal ) random.Between( 0.5, 1.8 ) * yearly / 12 / Math.Max( 1, pattern.Funds[generalFund] );
                    var when = new DateTime( year, 12, day ).AddHours( random.Between( 8, 23 ) );
                    if ( channel.Currency == "Check" )
                    {
                        AddOffSunday( when, "Check", multiplier, generalFund );
                    }
                    else
                    {
                        Add( when, false, random.Chance( 0.5 ) ? "Credit Card" : "ACH", null, multiplier, generalFund );
                    }
                }
            }

            // The largest givers now and then give stock or property, mostly in the fall.
            if ( yearly >= NonCashYearlyMinimum && pattern.Persona != "one-time" )
            {
                for ( var year = first.Year; year <= last.Year; year++ )
                {
                    if ( random.Chance( NonCashShare ) )
                    {
                        var when = new DateTime( year, random.Chance( 0.6 ) ? 12 : random.Between( 9, 11 ), random.Between( 1, 28 ) ).AddHours( random.Between( 9, 16 ) );
                        Add( when, false, "Non-Cash Asset", null, ( decimal ) random.Between( 0.2, 0.6 ) * yearly / Math.Max( 1, pattern.Funds[generalFund] ), generalFund );
                    }
                }
            }

            if ( Campaign != null && Campaign.TryGetValue( pattern, out var campaign ) )
            {
                AddCampaignGiving( campaign );
            }

            return gifts.OrderBy( g => g.When ).ToList();

            /*
                10/22/26 - CLAUDE

                The campaign line is sized from the usual gifts this pattern really gave since the pledge started:
                the pledge's share for that time, spread over those gifts. So an on-pace giver is on pace however
                often they missed a Sunday, and a catch-up check carries the weeks it covers. The line uses no
                random numbers, so every other gift stays exactly as it was. An extra gift rides on the next usual
                hand-given gift within a month, as people add to their check, and otherwise is a gift of its own.

                Reason: Pledge stories hold on any install day (docs/pledges.md).
            */
            void AddCampaignGiving( CampaignGiving campaign )
            {
                var accountId = AccountId( campaign.Fund, household.CampusKey );
                var lineEnd = campaign.LineStop.HasValue && campaign.LineStop < last ? campaign.LineStop.Value : last;
                var usual = gifts.Where( g => g.Units > 0 && g.When >= campaign.LineStart && g.When <= lineEnd ).ToList();
                var observed = usual.Sum( g => g.Units );
                if ( campaign.Total.HasValue && campaign.LineFactor > 0 && observed > 0 )
                {
                    var share = ( decimal ) ( ( lineEnd - campaign.LineStart ).TotalDays / ( campaign.CampaignEnd - campaign.LineStart ).TotalDays );
                    var line = campaign.Total.Value * share / observed * campaign.LineFactor;
                    foreach ( var gift in usual )
                    {
                        gift.Amounts[accountId] = RoundGift( line * gift.Units, exact: false );
                    }
                }

                foreach ( var extra in campaign.Gifts.Where( g => g.When >= first && g.When <= last ) )
                {
                    if ( extra.Stock )
                    {
                        gifts.Add( new Gift( extra.When, false, "Non-Cash Asset", null, new Dictionary<int, decimal> { [accountId] = extra.Amount }, 0 ) );
                        continue;
                    }

                    var withUsual = channel.Scheduled ? null : gifts.Where( g => g.Units > 0 && g.When >= extra.When && g.When <= extra.When.AddDays( 31 ) ).OrderBy( g => g.When ).FirstOrDefault();
                    if ( withUsual != null )
                    {
                        withUsual.Amounts[accountId] = withUsual.Amounts.GetValueOrDefault( accountId ) + extra.Amount;
                        continue;
                    }

                    var placed = OffSunday( extra.When, "Credit Card" );
                    if ( placed.When <= last )
                    {
                        gifts.Add( new Gift( placed.When, placed.Onsite, placed.Currency, null, new Dictionary<int, decimal> { [accountId] = extra.Amount }, 0 ) );
                    }
                }
            }
        }

        // Tuning knobs for gift amounts and timing, found by running the validation loop.
        private const double StrayShareMin = 0.05;
        private const double StrayShareMax = 0.6;
        private const double CatchUpShare = 0.6;
        private const double UsualWeekdayShare = 0.85;
        private const double ExactAmountShare = 0.1;
        private const double LooseAmountSpread = 0.8;
        private const double LooseUsualShare = 0.4;
        private const decimal IrregularScheduleAmount = 0.6m;
        private const double ScheduleChangesPerYear = 0.8;
        private const decimal NonCashYearlyMinimum = 20000;
        private const double NonCashShare = 0.06;

        /// <summary>
        /// How a hand-given gift strays from the usual amount, by weight: a smaller week, a bigger one, a double.
        /// </summary>
        private static readonly Dictionary<string, double> StrayFactors = new Dictionary<string, double>
        {
            ["0.5"] = 2,
            ["0.75"] = 2,
            ["1.25"] = 1,
            ["1.5"] = 2,
            ["2"] = 2,
            ["3"] = 0.7
        };

        /// <summary>
        /// When a schedule's amount changes, and the running factor from then on: mostly raises, sometimes a cut.
        /// </summary>
        private static List<(DateTime When, decimal Factor)> ScheduleChanges( DateTime first, DateTime last, SeededRandom random )
        {
            var changes = new List<(DateTime When, decimal Factor)>();
            var factor = 1m;
            for ( var year = first.AddMonths( 6 ); year < last; year = year.AddYears( 1 ) )
            {
                if ( random.Chance( ScheduleChangesPerYear ) )
                {
                    factor *= ( decimal ) ( random.Chance( 0.75 ) ? random.Between( 1.1, 1.6 ) : random.Between( 0.5, 0.9 ) );
                    changes.Add( (year.AddDays( random.Between( 0, 180 ) ), factor) );
                }
            }

            return changes.OrderBy( c => c.When ).ToList();
        }

        /// <summary>
        /// About how many gifts a pattern gives in a month.
        /// </summary>
        private static decimal GiftsPerMonth( string persona )
        {
            return persona switch
            {
                "weekly" => 4.33m,
                "bi-weekly" => 2.17m,
                "irregular" => 1.5m,
                "monthly" => 1m,
                "quarterly" => 0.33m,
                _ => 0.2m
            };
        }

        /// <summary>
        /// The growth factor for a gift: the pattern's percent, compounded every so many weeks since it started.
        /// </summary>
        private static decimal Growth( CastGiving pattern, DateTime started, DateTime when )
        {
            if ( pattern.Growth == null || pattern.Growth.EveryWeeks <= 0 || pattern.Growth.Percent == 0 )
            {
                return 1;
            }

            var steps = Math.Max( 0, ( int ) ( ( when - started ).TotalDays / 7 / pattern.Growth.EveryWeeks ) );
            return ( decimal ) Math.Pow( 1 + ( double ) pattern.Growth.Percent / 100, steps );
        }

        /// <summary>
        /// Rounds a gift the way people give: whole dollars, multiples of 5 from $20, and rounder steps as gifts grow.
        /// A giver who gives exact amounts keeps whole dollars at any size.
        /// </summary>
        private static decimal RoundGift( decimal amount, bool exact )
        {
            if ( amount >= 20 && !exact )
            {
                // Bigger gifts are rounder: people write $250 or $100, not $245.
                var step = amount >= 400 ? 100m : amount >= 150 ? 25m : amount >= 40 ? 10m : 5m;
                return Math.Max( 5, Math.Round( amount / step, MidpointRounding.AwayFromZero ) * step );
            }

            return Math.Max( 1, Math.Round( amount, MidpointRounding.AwayFromZero ) );
        }

        /// <summary>
        /// The given weekday in the Monday-to-Sunday week that ends on a Sunday.
        /// </summary>
        private static DateTime WeekdayInWeekEnding( DateTime sunday, string weekday )
        {
            var index = Array.IndexOf( Weekdays, weekday );
            return index == 0 ? sunday : sunday.AddDays( index - 7 );
        }

        /// <summary>
        /// The account for a fund name: its campus child account when the fund has them.
        /// </summary>
        private int AccountId( string fundName, string campusKey )
        {
            if ( _accountIds.TryGetValue( (fundName, campusKey), out var id ) )
            {
                return id;
            }

            var fund = _source.Accounts.Accounts.FirstOrDefault( a => a.Name == fundName ) ?? _source.Accounts.Accounts.First();
            var child = fund.CampusAccounts.FirstOrDefault( c => c.Campus == campusKey );
            id = _db.RequireIdByGuid( "FinancialAccount", child?.Guid ?? fund.Guid );
            _accountIds[(fundName, campusKey)] = id;
            return id;
        }

        private Dictionary<string, double> PaymentMix()
        {
            var mix = new Dictionary<string, double>();
            if ( _profile.Node( "giving.paymentMix.current" ) is System.Text.Json.Nodes.JsonObject current )
            {
                foreach ( var kv in current )
                {
                    mix[kv.Key] = kv.Value?["pctGifts"]?.GetValue<double>() ?? 0;
                }
            }

            return mix;
        }

        private Dictionary<string, double> WeekdayWeights( string kind )
        {
            var weights = new Dictionary<string, double>();
            if ( _profile.Node( "giving.dayOfWeekMix" ) is System.Text.Json.Nodes.JsonObject days )
            {
                foreach ( var kv in days )
                {
                    weights[kv.Key] = Math.Max( 0.1, kv.Value?[kind]?.GetValue<double>() ?? 0 );
                }
            }

            return weights.Count > 0 ? weights : Weekdays.ToDictionary( d => d, _ => 1.0 );
        }

        private Dictionary<string, double> ScheduledDayOfMonthWeights()
        {
            var weights = _profile.Map( "giving.scheduledDayOfMonthMix" );
            return weights.Count > 0 ? weights : Enumerable.Range( 1, 28 ).ToDictionary( d => d.ToString(), _ => 1.0 );
        }

        /// <summary>
        /// The day of the month a monthly or quarterly schedule runs on.
        /// </summary>
        /// <remarks>
        /// The profile's day-of-month mix counts every scheduled gift, and weekly and bi-weekly
        /// schedules land on every day about evenly. So the monthly weights are sharpened: each day
        /// loses the even share the weekly schedules give it, and the rest is scaled up by the share
        /// of scheduled gifts that are monthly.
        /// </remarks>
        private Dictionary<string, double> MonthlyScheduleDayWeights()
        {
            _monthlyScheduleDayWeights ??= Sharpen( ScheduledDayOfMonthWeights(), 30.4, MonthlyScheduledShare() );
            return _monthlyScheduleDayWeights;
        }

        /// <summary>
        /// The weekday a weekly or bi-weekly schedule runs on, sharpened the same way: monthly
        /// schedules land on every weekday about evenly.
        /// </summary>
        private Dictionary<string, double> WeeklyScheduleWeekdayWeights()
        {
            _weeklyScheduleWeekdayWeights ??= Sharpen( WeekdayWeights( "scheduled" ), 7, 1 - MonthlyScheduledShare() );
            return _weeklyScheduleWeekdayWeights;
        }

        private Dictionary<string, double> _monthlyScheduleDayWeights;
        private Dictionary<string, double> _weeklyScheduleWeekdayWeights;

        /// <summary>
        /// The share of scheduled gifts that come from monthly schedules (monthly, quarterly, and the
        /// small schedules under irregular givers), from the profile's cadence mix.
        /// </summary>
        private double MonthlyScheduledShare()
        {
            double ScheduledGifts( string cadence )
            {
                var path = $"giving.cadenceMix.{cadence}";
                return _profile.Number( $"{path}.pctUnits", 0 ) * _profile.Number( $"{path}.avgGiftDays", 0 ) * _profile.Number( $"{path}.pctGiftsScheduled", 0 );
            }

            var monthly = ScheduledGifts( "Monthly" ) + ScheduledGifts( "Quarterly" ) + ScheduledGifts( "Irregular" );
            var all = monthly + ScheduledGifts( "Weekly" ) + ScheduledGifts( "Bi-weekly or twice monthly" );
            return all > 0 ? monthly / all : 1;
        }

        /// <summary>
        /// Sharpens a measured mix for the part of it one kind of schedule makes: each slot loses the even
        /// share the other kind gives it, and the rest is scaled up by this kind's share.
        /// </summary>
        private static Dictionary<string, double> Sharpen( Dictionary<string, double> measured, double slots, double share )
        {
            var even = measured.Values.Sum() / slots;
            return measured.ToDictionary( m => m.Key, m => Math.Max( even * 0.05, ( m.Value - ( ( 1 - share ) * even ) ) / Math.Max( 0.05, share ) ) );
        }

        private object CardBrand( string currency, Wallet wallet )
        {
            return currency == "Credit Card" ? DefinedValueIdByName( "Credit Card Type", wallet.CardBrand ) : DBNull.Value;
        }

        /// <summary>
        /// The giver's wallet, created on first use, so every pattern of one giver shares it.
        /// </summary>
        private Wallet WalletFor( LoadedPerson giver )
        {
            if ( !_wallets.TryGetValue( giver.PersonId, out var wallet ) )
            {
                wallet = new Wallet( _seed, giver.Cast.Guid );
                _wallets[giver.PersonId] = wallet;
            }

            return wallet;
        }

        /*
            10/16/26 - CLAUDE

            Rock's transaction matching works through scanned checks that do not yet say who gave them.
            One recent Sunday check batch at the main campus stays open, as if it was scanned and nobody
            has matched it yet: its checks keep their amounts, check numbers, images, and bank line, but
            have no giver. Each writer's bank account is on file, so matching suggests the right person.
            The main campus holds the Cast, so the batch has familiar names in every dataset. Cast
            households with giving expectations keep their checks matched, because one missing gift can
            change what Rock shows for them.

            Reason: Every dataset needs an open batch of unmatched checks to try check matching with.
        */

        /// <summary>
        /// Leaves the main campus Sunday check batch with the most checks in the last <see cref="OpenBatchWeeks"/> weeks
        /// open, with its checks unmatched. Returns the Ids of the unmatched transactions.
        /// </summary>
        private HashSet<int> LeaveRecentCheckBatchUnmatched( DataTable batches, Dictionary<(DateTime Date, int Campus, string Kind), int> batchIds )
        {
            var mainCampusId = _configuration.CampusPlans[_source.Campuses.Campuses.Single( c => c.RockDefault ).Key].CampusId;
            var offerings = batchIds.Where( b => b.Key.Kind == "onsite-checks" && b.Key.Campus == mainCampusId ).ToDictionary( b => b.Value, b => b.Key.Date );
            // A Cast household whose giving story says what Rock shows keeps every gift, or one unmatched gift could
            // change its frequency or journey stage. Its checks in the open batch stay matched.
            var checks = CheckGifts
                .Where( c => offerings.ContainsKey( ( int ) c.TransactionRow["BatchId"] ) )
                .Where( c => c.Household.Cast?.Giving.Any( g => g.Expect != null ) != true )
                .ToList();
            if ( checks.Count == 0 )
            {
                return new HashSet<int>();
            }

            // A small dataset can have a Sunday with one check, which is too few to try matching with, so the batch is
            // the recent Sunday with the most checks (the newest of those that tie), not simply the newest.
            var newest = checks.Max( c => offerings[( int ) c.TransactionRow["BatchId"]] );
            var batchId = checks
                .GroupBy( c => ( int ) c.TransactionRow["BatchId"] )
                .Where( g => offerings[g.Key] > newest.AddDays( -7 * OpenBatchWeeks ) )
                .OrderByDescending( g => g.Count() )
                .ThenByDescending( g => offerings[g.Key] )
                .First().Key;
            var batch = batches.AsEnumerable().First( r => ( int ) r["Id"] == batchId );
            batch["Status"] = 1;

            var unmatched = new HashSet<int>();
            foreach ( var check in checks.Where( c => ( int ) c.TransactionRow["BatchId"] == batchId ) )
            {
                check.TransactionRow["AuthorizedPersonAliasId"] = DBNull.Value;
                unmatched.Add( check.TransactionId );
            }

            _openBatchLine = $"  Open batch: {batch["Name"]}, with {unmatched.Count:N0} unmatched check(s) for transaction matching";
            return unmatched;
        }

        /// <summary>
        /// Numbers each writer's checks in date order from their checkbook, and stores the number as the transaction code.
        /// </summary>
        private void AssignCheckNumbers()
        {
            foreach ( var writer in CheckGifts.GroupBy( c => c.Giver.PersonId ) )
            {
                foreach ( var check in writer.OrderBy( c => c.When ) )
                {
                    check.CheckNumber = check.Wallet.TakeCheckNumber();
                    check.TransactionRow["TransactionCode"] = check.CheckNumber.ToString();

                    // The raw bank line a scanner reads, hashed the way Rock does so a rescan is caught as a duplicate.
                    // With the DataEncryptionKey, the bank line and its parts are also stored encrypted, the way
                    // Rock's check scanning stores them; transaction matching reads the parts to suggest the writer.
                    if ( Keys != null )
                    {
                        var bankLine = $"d{check.Wallet.RoutingNumber}d{check.Wallet.AccountNumber}c{check.CheckNumber}";
                        check.TransactionRow["CheckMicrHash"] = Keys.Hash( bankLine );
                        check.TransactionRow["MICRStatus"] = 0;
                        if ( Keys.CanEncrypt )
                        {
                            check.TransactionRow["CheckMicrEncrypted"] = Keys.EncryptString( bankLine );
                            check.TransactionRow["CheckMicrParts"] = Keys.EncryptString( $"{check.Wallet.RoutingNumber}_{check.Wallet.AccountNumber}_{check.CheckNumber}" );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// One bank account row per check writer, so Rock's check scanner finds the writer when one of
        /// their checks is scanned. Returns null when no PasswordKey is available.
        /// </summary>
        private DataTable CheckWriterBankAccounts( DateTime now )
        {
            if ( Keys == null )
            {
                return null;
            }

            var rows = TableFactory.Create( "Id:int", "PersonAliasId:int", "AccountNumberSecured:string", "AccountNumberMasked:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var nextId = NextId( "FinancialPersonBankAccount" );
            foreach ( var writer in CheckGifts.GroupBy( c => c.Giver.PersonId ) )
            {
                var first = writer.OrderBy( c => c.When ).First();
                var wallet = first.Wallet;
                rows.Rows.Add( nextId++, first.Giver.AliasId, Keys.BankAccountHash( wallet.RoutingNumber, wallet.AccountNumber ), wallet.Masked( "Check" ), Guid.NewGuid(), _foreignKey, first.When, now );
            }

            return rows;
        }

        /// <summary>
        /// The fund name for an account Id this loader looked up.
        /// </summary>
        private string FundName( int accountId )
        {
            return _accountIds.First( a => a.Value == accountId ).Key.Fund;
        }

        private int DefinedValueId( Guid guid )
        {
            var key = guid.ToString();
            if ( !_definedValueIds.TryGetValue( key, out var id ) )
            {
                id = _db.RequireIdByGuid( "DefinedValue", guid );
                _definedValueIds[key] = id;
            }

            return id;
        }

        private object DefinedValueIdByName( string definedTypeName, string value )
        {
            var key = $"{definedTypeName}:{value}";
            if ( !_definedValueIds.TryGetValue( key, out var id ) )
            {
                var found = _db.Scalar( "SELECT TOP 1 [dv].[Id] FROM [DefinedValue] AS [dv] INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId] WHERE [dt].[Name] = @Type AND [dv].[Value] = @Value",
                    RockDatabase.P( "@Type", definedTypeName ), RockDatabase.P( "@Value", value ) );
                if ( found == null )
                {
                    return DBNull.Value;
                }

                id = Convert.ToInt32( found );
                _definedValueIds[key] = id;
            }

            return id;
        }

        private int NextId( string table )
        {
            return Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
        }
    }

    /// <summary>
    /// A check gift and everything its scanned image shows.
    /// </summary>
    internal sealed class CheckGift
    {
        public CheckGift( int transactionId, DataRow transactionRow, LoadedPerson giver, LoadedHousehold household, Wallet wallet, DateTime when, decimal amount, List<string> funds, string signer )
        {
            TransactionId = transactionId;
            TransactionRow = transactionRow;
            Giver = giver;
            Household = household;
            Wallet = wallet;
            When = when;
            Amount = amount;
            Funds = funds;
            Signer = signer;
        }

        public int TransactionId { get; }

        public DataRow TransactionRow { get; }

        public LoadedPerson Giver { get; }

        public LoadedHousehold Household { get; }

        public Wallet Wallet { get; }

        /// <summary>
        /// When the check went into the offering (the transaction date).
        /// </summary>
        public DateTime When { get; }

        public decimal Amount { get; }

        public List<string> Funds { get; }

        public int CheckNumber { get; set; }

        /// <summary>
        /// Who signs the check when it is not the giver (for example a business owner). Null means the giver signs.
        /// </summary>
        public string Signer { get; }
    }
}
