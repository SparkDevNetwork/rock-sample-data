SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 1 of 3: people and giving.
--
--              Collects anonymous, aggregate statistics from a live Rock
--              database. The output calibrates the sample data simulator so
--              its patterns match measured patterns. Returns no names, no Ids, and
--              no personal data.
--
--              HOW TO RUN IN ROCK:
--              1. Open Admin Tools > Settings > Power Tools > SQL Command.
--              2. Leave "Selection Query?" set to Yes. Rock runs the script in
--                 a transaction and rolls it back, so nothing can change.
--              3. Paste this whole file and click Run.
--              4. Rock shows a yellow warning that the query "modifies data."
--                 That is expected. The script only uses temp tables, and Rock
--                 rolled them back.
--              5. Export the grid to Excel, or copy the rows. Send the result.
--
--              Use the same @ChurchLabel in all three parts.
--              Also runs in SSMS. Requires SQL Server 2016 or later.
-- Date: 2026-10-14
-- =============================================

-- Settings
DECLARE @ChurchLabel NVARCHAR(50) = 'Unlabeled'; -- Use a neutral label like source-2, never a church name.

-- The window is the last 24 complete months. The current month is excluded.
DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start12 DATE = DATEADD( MONTH, -12, @EndDate );
DECLARE @Start24 DATE = DATEADD( MONTH, -24, @EndDate );

-- Lookups
DECLARE @ContributionTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '2D607262-52D6-4724-910D-5C6E8FB89ACC' );
DECLARE @PersonRecordTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' );
DECLARE @BusinessRecordTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = 'BF64ADD3-E70A-44CE-9C4B-E76BBED37550' );
DECLARE @ActiveRecordStatusValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' );
DECLARE @HomeLocationTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC' );
DECLARE @MobilePhoneTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '407E7E45-7B2E-4FCD-9605-ECB1339F2453' );

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #Person, #Txn, #Gift, #GiftDay, #Unit12, #Gap, #UnitMedianGap, #UnitRhythm, #UnitModal, #UnitCadence;

-- =============================================
-- Base sets
-- =============================================

-- Active, living people (not businesses).
SELECT
    [p].[Id] AS [PersonId],
    [p].[PrimaryFamilyId],
    [p].[PrimaryCampusId],
    [p].[AgeClassification],
    [p].[Gender],
    [p].[BirthDate],
    CAST( FLOOR( DATEDIFF( DAY, [p].[BirthDate], @EndDate ) / 365.25 ) AS INT ) AS [Age],
    [p].[GraduationYear],
    [p].[ConnectionStatusValueId],
    [p].[MaritalStatusValueId],
    CASE WHEN ISNULL( [p].[Email], '' ) <> '' THEN 1 ELSE 0 END AS [HasEmail],
    CASE WHEN [p].[PhotoId] IS NOT NULL THEN 1 ELSE 0 END AS [HasPhoto]
INTO #Person
FROM [Person] AS [p]
WHERE [p].[RecordTypeValueId] = @PersonRecordTypeValueId
    AND [p].[RecordStatusValueId] = @ActiveRecordStatusValueId
    AND [p].[IsDeceased] = 0;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Person ( [PersonId] );

-- Contribution transactions in the 24 month window.
SELECT
    [ft].[Id] AS [TransactionId],
    [ft].[AuthorizedPersonAliasId],
    [ft].[TransactionDateTime],
    [ft].[ScheduledTransactionId],
    [ft].[SourceTypeValueId],
    [ft].[FinancialPaymentDetailId]
INTO #Txn
FROM [FinancialTransaction] AS [ft]
WHERE [ft].[TransactionTypeValueId] = @ContributionTypeValueId
    AND [ft].[TransactionDateTime] >= @Start24
    AND [ft].[TransactionDateTime] < @EndDate
    AND [ft].[AuthorizedPersonAliasId] IS NOT NULL;

CREATE CLUSTERED INDEX [IX_TransactionId] ON #Txn ( [TransactionId] );

-- One row per transaction with its giving unit, total amount, and fund count.
-- Includes gifts from inactive people, since lapsed givers matter.
SELECT
    [t].[TransactionId],
    [gp].[GivingId],
    CASE WHEN [gp].[RecordTypeValueId] = @BusinessRecordTypeValueId THEN 1 ELSE 0 END AS [IsBusiness],
    CAST( [t].[TransactionDateTime] AS DATE ) AS [GiftDate],
    CASE WHEN [t].[ScheduledTransactionId] IS NULL THEN 0 ELSE 1 END AS [IsScheduled],
    [t].[SourceTypeValueId],
    [fpd].[CurrencyTypeValueId],
    [d].[Amount],
    [d].[AccountCount]
INTO #Gift
FROM #Txn AS [t]
INNER JOIN (
    SELECT
        [ftd].[TransactionId],
        SUM( [ftd].[Amount] ) AS [Amount],
        COUNT( DISTINCT [ftd].[AccountId] ) AS [AccountCount]
    FROM [FinancialTransactionDetail] AS [ftd]
    INNER JOIN #Txn AS [t2] ON [t2].[TransactionId] = [ftd].[TransactionId]
    GROUP BY [ftd].[TransactionId]
) AS [d] ON [d].[TransactionId] = [t].[TransactionId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[AuthorizedPersonAliasId]
INNER JOIN [Person] AS [gp] ON [gp].[Id] = [pa].[PersonId]
LEFT JOIN [FinancialPaymentDetail] AS [fpd] ON [fpd].[Id] = [t].[FinancialPaymentDetailId];

-- Collapse to one row per giving unit per day. Two gifts on the same day
-- (for example, split across funds) count as one giving event.
SELECT
    [g].[GivingId],
    [g].[GiftDate],
    SUM( [g].[Amount] ) AS [Amount],
    MAX( [g].[IsScheduled] ) AS [IsScheduled],
    MAX( [g].[IsBusiness] ) AS [IsBusiness]
INTO #GiftDay
FROM #Gift AS [g]
WHERE [g].[Amount] > 0
GROUP BY [g].[GivingId], [g].[GiftDate];

CREATE CLUSTERED INDEX [IX_GivingId_GiftDate] ON #GiftDay ( [GivingId], [GiftDate] );

-- Giving unit summary for the last 12 months.
SELECT
    [gd].[GivingId],
    COUNT( * ) AS [GiftDays],
    SUM( [gd].[Amount] ) AS [Total],
    CASE WHEN COUNT( * ) > 1 AND AVG( [gd].[Amount] ) > 0 THEN STDEV( [gd].[Amount] ) / AVG( [gd].[Amount] ) END AS [AmountCv],
    MAX( [gd].[IsBusiness] ) AS [IsBusiness],
    SUM( [gd].[IsScheduled] ) AS [ScheduledDays]
INTO #Unit12
FROM #GiftDay AS [gd]
WHERE [gd].[GiftDate] >= @Start12
GROUP BY [gd].[GivingId];

CREATE CLUSTERED INDEX [IX_GivingId] ON #Unit12 ( [GivingId] );

-- Days between giving events, per unit.
SELECT
    [gd].[GivingId],
    DATEDIFF( DAY, LAG( [gd].[GiftDate] ) OVER ( PARTITION BY [gd].[GivingId] ORDER BY [gd].[GiftDate] ), [gd].[GiftDate] ) AS [Gap]
INTO #Gap
FROM #GiftDay AS [gd]
WHERE [gd].[GiftDate] >= @Start12;

DELETE FROM #Gap WHERE [Gap] IS NULL;

CREATE CLUSTERED INDEX [IX_GivingId] ON #Gap ( [GivingId], [Gap] );

-- Median gap per unit. ROW_NUMBER is much faster than a windowed
-- PERCENTILE_CONT on large tables.
SELECT
    [o].[GivingId],
    AVG( [o].[Gap] * 1.0 ) AS [MedianGap]
INTO #UnitMedianGap
FROM (
    SELECT
        [g].[GivingId],
        [g].[Gap],
        ROW_NUMBER() OVER ( PARTITION BY [g].[GivingId] ORDER BY [g].[Gap] ) AS [RowNumber],
        COUNT( * ) OVER ( PARTITION BY [g].[GivingId] ) AS [RowCount]
    FROM #Gap AS [g]
) AS [o]
WHERE [o].[RowNumber] IN ( ( [o].[RowCount] + 1 ) / 2, ( [o].[RowCount] + 2 ) / 2 )
GROUP BY [o].[GivingId];

CREATE CLUSTERED INDEX [IX_GivingId] ON #UnitMedianGap ( [GivingId] );

-- Share of gaps within 25% of the unit's median gap. High = steady rhythm.
SELECT
    [g].[GivingId],
    AVG( CASE WHEN ABS( [g].[Gap] - [m].[MedianGap] ) <= [m].[MedianGap] * 0.25 THEN 1.0 ELSE 0.0 END ) AS [OnRhythmPct]
INTO #UnitRhythm
FROM #Gap AS [g]
INNER JOIN #UnitMedianGap AS [m] ON [m].[GivingId] = [g].[GivingId]
GROUP BY [g].[GivingId];

-- Share of gifts at the unit's most common amount. High = same gift every time.
SELECT
    [a].[GivingId],
    MAX( [a].[AmountCount] ) * 1.0 / MAX( [u].[GiftDays] ) AS [ModalAmountShare]
INTO #UnitModal
FROM (
    SELECT [gd].[GivingId], [gd].[Amount], COUNT( * ) AS [AmountCount]
    FROM #GiftDay AS [gd]
    WHERE [gd].[GiftDate] >= @Start12
    GROUP BY [gd].[GivingId], [gd].[Amount]
) AS [a]
INNER JOIN #Unit12 AS [u] ON [u].[GivingId] = [a].[GivingId]
GROUP BY [a].[GivingId];

-- Classify each unit's giving cadence. A regular cadence needs 4+ giving
-- days and at least half of the gaps on rhythm. Everything else is irregular.
SELECT
    [u].[GivingId],
    [u].[GiftDays],
    [u].[Total],
    [u].[AmountCv],
    [u].[ScheduledDays],
    [r].[OnRhythmPct],
    [mo].[ModalAmountShare],
    CASE
        WHEN [u].[GiftDays] = 1 THEN '1 One time'
        WHEN [u].[GiftDays] <= 3 THEN '2 Occasional (2-3)'
        WHEN [r].[OnRhythmPct] < 0.5 THEN '7 Irregular'
        WHEN [m].[MedianGap] BETWEEN 5 AND 9 THEN '3 Weekly'
        WHEN [m].[MedianGap] BETWEEN 10 AND 18 THEN '4 Bi-weekly or twice monthly'
        WHEN [m].[MedianGap] BETWEEN 25 AND 38 THEN '5 Monthly'
        WHEN [m].[MedianGap] BETWEEN 80 AND 100 THEN '6 Quarterly'
        ELSE '7 Irregular'
    END AS [Cadence]
INTO #UnitCadence
FROM #Unit12 AS [u]
LEFT JOIN #UnitMedianGap AS [m] ON [m].[GivingId] = [u].[GivingId]
LEFT JOIN #UnitRhythm AS [r] ON [r].[GivingId] = [u].[GivingId]
LEFT JOIN #UnitModal AS [mo] ON [mo].[GivingId] = [u].[GivingId];

-- =============================================
-- People
-- =============================================

DECLARE @JPeopleSummary NVARCHAR(MAX) = (
    SELECT
        ( SELECT COUNT( * ) FROM #Person ) AS [activePeople],
        ( SELECT COUNT( DISTINCT [PrimaryFamilyId] ) FROM #Person ) AS [activeFamilies],
        ( SELECT COUNT( DISTINCT [PrimaryCampusId] ) FROM #Person ) AS [campusesInUse],
        ( SELECT CAST( SUM( CASE WHEN [PrimaryCampusId] IS NULL THEN 1 ELSE 0 END ) * 100.0 / NULLIF( COUNT( * ), 0 ) AS DECIMAL(5,1) ) FROM #Person ) AS [pctNoCampus],
        ( SELECT CAST( SUM( CASE WHEN [AgeClassification] = 1 THEN 1 ELSE 0 END ) * 100.0 / NULLIF( COUNT( * ), 0 ) AS DECIMAL(5,1) ) FROM #Person ) AS [pctAdults],
        ( SELECT CAST( SUM( CASE WHEN [AgeClassification] = 2 THEN 1 ELSE 0 END ) * 100.0 / NULLIF( COUNT( * ), 0 ) AS DECIMAL(5,1) ) FROM #Person ) AS [pctChildren]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JAgeGender NVARCHAR(MAX) = (
    SELECT
        [b].[AgeBucket] AS [ageBucket],
        SUM( CASE WHEN [b].[Gender] = 1 THEN 1 ELSE 0 END ) AS [male],
        SUM( CASE WHEN [b].[Gender] = 2 THEN 1 ELSE 0 END ) AS [female],
        SUM( CASE WHEN [b].[Gender] = 0 THEN 1 ELSE 0 END ) AS [unknown]
    FROM (
        SELECT
            [p].[Gender],
            CASE
                WHEN [p].[BirthDate] IS NULL THEN '99 Unknown'
                WHEN [p].[Age] < 6 THEN '00-05'
                WHEN [p].[Age] < 12 THEN '06-11'
                WHEN [p].[Age] < 18 THEN '12-17'
                WHEN [p].[Age] < 25 THEN '18-24'
                WHEN [p].[Age] < 35 THEN '25-34'
                WHEN [p].[Age] < 45 THEN '35-44'
                WHEN [p].[Age] < 55 THEN '45-54'
                WHEN [p].[Age] < 65 THEN '55-64'
                WHEN [p].[Age] < 75 THEN '65-74'
                ELSE '75+'
            END AS [AgeBucket]
        FROM #Person AS [p]
    ) AS [b]
    GROUP BY [b].[AgeBucket]
    ORDER BY [b].[AgeBucket]
    FOR JSON PATH
);

DECLARE @JConnectionStatus NVARCHAR(MAX) = (
    SELECT
        ISNULL( [dv].[Value], 'None' ) AS [connectionStatus],
        COUNT( * ) AS [people],
        SUM( CASE WHEN [p].[AgeClassification] = 1 THEN 1 ELSE 0 END ) AS [adults]
    FROM #Person AS [p]
    LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [p].[ConnectionStatusValueId]
    GROUP BY [dv].[Value]
    ORDER BY COUNT( * ) DESC
    FOR JSON PATH
);

DECLARE @JMaritalStatus NVARCHAR(MAX) = (
    SELECT
        ISNULL( [dv].[Value], 'None' ) AS [maritalStatus],
        COUNT( * ) AS [adults]
    FROM #Person AS [p]
    LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [p].[MaritalStatusValueId]
    WHERE [p].[AgeClassification] = 1
    GROUP BY [dv].[Value]
    ORDER BY COUNT( * ) DESC
    FOR JSON PATH
);

DECLARE @JFamilySize NVARCHAR(MAX) = (
    SELECT
        [f].[SizeBucket] AS [familySize],
        COUNT( * ) AS [families],
        CAST( AVG( [f].[Adults] * 1.0 ) AS DECIMAL(4,2) ) AS [avgAdults],
        CAST( AVG( [f].[Children] * 1.0 ) AS DECIMAL(4,2) ) AS [avgChildren]
    FROM (
        SELECT
            CASE WHEN COUNT( * ) >= 6 THEN '6+' ELSE CAST( COUNT( * ) AS VARCHAR(2) ) END AS [SizeBucket],
            SUM( CASE WHEN [p].[AgeClassification] = 1 THEN 1 ELSE 0 END ) AS [Adults],
            SUM( CASE WHEN [p].[AgeClassification] = 2 THEN 1 ELSE 0 END ) AS [Children]
        FROM #Person AS [p]
        WHERE [p].[PrimaryFamilyId] IS NOT NULL
        GROUP BY [p].[PrimaryFamilyId]
    ) AS [f]
    GROUP BY [f].[SizeBucket]
    ORDER BY [f].[SizeBucket]
    FOR JSON PATH
);

-- How complete real records are. Good sample data has realistic gaps.
DECLARE @JCompleteness NVARCHAR(MAX) = (
    SELECT
        CASE [p].[AgeClassification] WHEN 1 THEN 'Adult' WHEN 2 THEN 'Child' ELSE 'Unknown' END AS [ageClassification],
        COUNT( * ) AS [people],
        CAST( AVG( [p].[HasEmail] * 100.0 ) AS DECIMAL(5,1) ) AS [pctEmail],
        CAST( AVG( CASE WHEN [mp].[PersonId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMobilePhone],
        CAST( AVG( CASE WHEN [mp].[IsMessagingEnabled] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctSmsEnabled],
        CAST( AVG( CASE WHEN [p].[BirthDate] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctBirthDate],
        CAST( AVG( CASE WHEN [p].[GraduationYear] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctGraduationYear],
        CAST( AVG( CASE WHEN [p].[MaritalStatusValueId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMaritalStatus],
        CAST( AVG( [p].[HasPhoto] * 100.0 ) AS DECIMAL(5,1) ) AS [pctPhoto],
        CAST( AVG( CASE WHEN [hl].[GroupId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctHomeAddress]
    FROM #Person AS [p]
    OUTER APPLY (
        SELECT TOP 1 [pn].[PersonId], [pn].[IsMessagingEnabled]
        FROM [PhoneNumber] AS [pn]
        WHERE [pn].[PersonId] = [p].[PersonId]
            AND [pn].[NumberTypeValueId] = @MobilePhoneTypeValueId
        ORDER BY [pn].[IsMessagingEnabled] DESC
    ) AS [mp]
    OUTER APPLY (
        SELECT TOP 1 [gl].[GroupId]
        FROM [GroupLocation] AS [gl]
        WHERE [gl].[GroupId] = [p].[PrimaryFamilyId]
            AND [gl].[GroupLocationTypeValueId] = @HomeLocationTypeValueId
    ) AS [hl]
    GROUP BY [p].[AgeClassification]
    ORDER BY [p].[AgeClassification]
    FOR JSON PATH
);

-- New person records per month (all statuses, so drop-off is visible).
DECLARE @JNewPeopleByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [p].[CreatedDateTime], 120 ) AS [month],
        COUNT( * ) AS [newPeople]
    FROM [Person] AS [p]
    WHERE [p].[RecordTypeValueId] = @PersonRecordTypeValueId
        AND [p].[CreatedDateTime] >= @Start24
        AND [p].[CreatedDateTime] < @EndDate
    GROUP BY CONVERT( CHAR(7), [p].[CreatedDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [p].[CreatedDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Giving
-- =============================================

DECLARE @JGivingSummary NVARCHAR(MAX) = (
    SELECT
        ( SELECT COUNT( * ) FROM #Unit12 ) AS [givingUnitsLast12],
        ( SELECT COUNT( DISTINCT [GivingId] ) FROM #GiftDay WHERE [GiftDate] < @Start12 ) AS [givingUnitsPrior12],
        ( SELECT COUNT( * ) FROM #Unit12 AS [u] WHERE EXISTS ( SELECT 1 FROM #GiftDay AS [gd] WHERE [gd].[GivingId] = [u].[GivingId] AND [gd].[GiftDate] < @Start12 ) ) AS [retainedUnits],
        ( SELECT COUNT( * ) FROM #Unit12 AS [u] WHERE NOT EXISTS ( SELECT 1 FROM #GiftDay AS [gd] WHERE [gd].[GivingId] = [u].[GivingId] AND [gd].[GiftDate] < @Start12 ) ) AS [newUnits],
        ( SELECT COUNT( DISTINCT [gd].[GivingId] ) FROM #GiftDay AS [gd] WHERE [gd].[GiftDate] < @Start12 AND NOT EXISTS ( SELECT 1 FROM #Unit12 AS [u] WHERE [u].[GivingId] = [gd].[GivingId] ) ) AS [lapsedUnits],
        ( SELECT CAST( SUM( [IsBusiness] ) * 100.0 / NULLIF( COUNT( * ), 0 ) AS DECIMAL(5,1) ) FROM #Unit12 ) AS [pctUnitsBusiness],
        ( SELECT CAST( SUM( CASE WHEN [IsBusiness] = 1 THEN [Total] ELSE 0 END ) * 100.0 / NULLIF( SUM( [Total] ), 0 ) AS DECIMAL(5,1) ) FROM #Unit12 ) AS [pctDollarsBusiness],
        ( SELECT CAST( SUM( CASE WHEN [ScheduledDays] > 0 THEN 1 ELSE 0 END ) * 100.0 / NULLIF( COUNT( * ), 0 ) AS DECIMAL(5,1) ) FROM #Unit12 ) AS [pctUnitsWithScheduledGift],
        ( SELECT COUNT( * ) FROM #Gift WHERE [GiftDate] >= @Start12 AND [Amount] > 0 ) AS [giftTransactionsLast12],
        ( SELECT COUNT( * ) FROM #Gift WHERE [GiftDate] >= @Start12 AND [Amount] < 0 ) AS [refundTransactionsLast12],
        ( SELECT CAST( SUM( [Total] ) AS DECIMAL(14,0) ) FROM #Unit12 ) AS [totalDollarsLast12],
        ( SELECT COUNT( * ) FROM [FinancialPledge] AS [fp] WHERE [fp].[StartDate] < @EndDate AND [fp].[EndDate] >= @Start12 ) AS [pledgesActiveLast12],
        (
            SELECT CAST( COUNT( DISTINCT [pp].[GivingId] ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Unit12 ), 0 ) AS DECIMAL(5,1) )
            FROM [FinancialPledge] AS [fp]
            INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [fp].[PersonAliasId]
            INNER JOIN [Person] AS [pp] ON [pp].[Id] = [pa].[PersonId]
            WHERE [fp].[StartDate] < @EndDate AND [fp].[EndDate] >= @Start12
        ) AS [pctUnitsWithPledge]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JGivingCadence NVARCHAR(MAX) = (
    SELECT
        [uc].[Cadence] AS [cadence],
        COUNT( * ) AS [units],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctUnits],
        CAST( SUM( [uc].[Total] ) * 100.0 / SUM( SUM( [uc].[Total] ) ) OVER () AS DECIMAL(5,1) ) AS [pctDollars],
        CAST( AVG( [uc].[GiftDays] * 1.0 ) AS DECIMAL(6,1) ) AS [avgGiftDays],
        CAST( AVG( [uc].[Total] ) AS DECIMAL(12,0) ) AS [avgAnnualTotal],
        CAST( AVG( [uc].[AmountCv] ) AS DECIMAL(6,2) ) AS [avgAmountVariation],
        CAST( AVG( [uc].[ModalAmountShare] ) * 100 AS DECIMAL(5,1) ) AS [avgPctGiftsAtUsualAmount],
        CAST( AVG( [uc].[OnRhythmPct] ) * 100 AS DECIMAL(5,1) ) AS [avgPctGapsOnRhythm],
        CAST( SUM( [uc].[ScheduledDays] ) * 100.0 / NULLIF( SUM( [uc].[GiftDays] ), 0 ) AS DECIMAL(5,1) ) AS [pctGiftsScheduled]
    FROM #UnitCadence AS [uc]
    GROUP BY [uc].[Cadence]
    ORDER BY [uc].[Cadence]
    FOR JSON PATH
);

-- Percentiles use nearest rank (one sort) instead of PERCENTILE_CONT (one sort per value).
DECLARE @JUnitAnnualTotals NVARCHAR(MAX) = (
    SELECT
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.10 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p10],
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.25 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p25],
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.50 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p50],
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.75 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p75],
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.90 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p90],
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.95 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p95],
        CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.99 ) THEN [o].[Total] END ) AS DECIMAL(12,2) ) AS [p99]
    FROM (
        SELECT
            [u].[Total],
            ROW_NUMBER() OVER ( ORDER BY [u].[Total] ) AS [RowNumber],
            COUNT( * ) OVER () AS [RowCount]
        FROM #Unit12 AS [u]
    ) AS [o]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- Share of dollars by giving decile (1 = top 10% of units).
DECLARE @JGivingDeciles NVARCHAR(MAX) = (
    SELECT
        [r].[Decile] AS [decile],
        CAST( SUM( [r].[Total] ) * 100.0 / SUM( SUM( [r].[Total] ) ) OVER () AS DECIMAL(5,1) ) AS [pctDollars]
    FROM (
        SELECT [u].[Total], NTILE( 10 ) OVER ( ORDER BY [u].[Total] DESC ) AS [Decile]
        FROM #Unit12 AS [u]
    ) AS [r]
    GROUP BY [r].[Decile]
    ORDER BY [r].[Decile]
    FOR JSON PATH
);

DECLARE @JTopOnePercent NVARCHAR(MAX) = (
    SELECT
        CAST( SUM( CASE WHEN [r].[Percentile] = 1 THEN [r].[Total] ELSE 0 END ) * 100.0 / NULLIF( SUM( [r].[Total] ), 0 ) AS DECIMAL(5,1) ) AS [pctDollarsTop1PctUnits]
    FROM (
        SELECT [u].[Total], NTILE( 100 ) OVER ( ORDER BY [u].[Total] DESC ) AS [Percentile]
        FROM #Unit12 AS [u]
    ) AS [r]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- Individual gift amounts (transactions, last 12 months).
DECLARE @JGiftAmounts NVARCHAR(MAX) = (
    SELECT [pc].*, [rd].*
    FROM (
        SELECT
            CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.10 ) THEN [o].[Amount] END ) AS DECIMAL(12,2) ) AS [p10],
            CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.25 ) THEN [o].[Amount] END ) AS DECIMAL(12,2) ) AS [p25],
            CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.50 ) THEN [o].[Amount] END ) AS DECIMAL(12,2) ) AS [p50],
            CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.75 ) THEN [o].[Amount] END ) AS DECIMAL(12,2) ) AS [p75],
            CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.90 ) THEN [o].[Amount] END ) AS DECIMAL(12,2) ) AS [p90],
            CAST( MAX( CASE WHEN [o].[RowNumber] = CEILING( [o].[RowCount] * 0.99 ) THEN [o].[Amount] END ) AS DECIMAL(12,2) ) AS [p99]
        FROM (
            SELECT
                [g].[Amount],
                ROW_NUMBER() OVER ( ORDER BY [g].[Amount] ) AS [RowNumber],
                COUNT( * ) OVER () AS [RowCount]
            FROM #Gift AS [g]
            WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0
        ) AS [o]
    ) AS [pc]
    CROSS JOIN (
        SELECT
            CAST( AVG( CASE WHEN [g].[Amount] <> FLOOR( [g].[Amount] ) THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCents],
            CAST( AVG( CASE WHEN [g].[Amount] % 5 = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMultipleOf5],
            CAST( AVG( CASE WHEN [g].[Amount] % 10 = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMultipleOf10],
            CAST( AVG( CASE WHEN [g].[Amount] % 25 = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMultipleOf25],
            CAST( AVG( CASE WHEN [g].[Amount] % 100 = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMultipleOf100]
        FROM #Gift AS [g]
        WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0
    ) AS [rd]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JGivingByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [g].[GiftDate], 120 ) AS [month],
        COUNT( * ) AS [gifts],
        COUNT( DISTINCT [g].[GivingId] ) AS [units],
        CAST( SUM( [g].[Amount] ) AS DECIMAL(14,0) ) AS [amount]
    FROM #Gift AS [g]
    WHERE [g].[Amount] > 0
    GROUP BY CONVERT( CHAR(7), [g].[GiftDate], 120 )
    ORDER BY CONVERT( CHAR(7), [g].[GiftDate], 120 )
    FOR JSON PATH
);

-- Day of week (0 = Sunday). 1900-01-07 was a Sunday, so this ignores DATEFIRST.
DECLARE @JGiftDayOfWeek NVARCHAR(MAX) = (
    SELECT
        DATEDIFF( DAY, '19000107', [g].[GiftDate] ) % 7 AS [dayOfWeek],
        CAST( SUM( CASE WHEN [g].[IsScheduled] = 0 THEN 1 ELSE 0 END ) * 100.0 / NULLIF( SUM( SUM( CASE WHEN [g].[IsScheduled] = 0 THEN 1 ELSE 0 END ) ) OVER (), 0 ) AS DECIMAL(5,1) ) AS [pctOneTimeGifts],
        CAST( SUM( [g].[IsScheduled] ) * 100.0 / NULLIF( SUM( SUM( [g].[IsScheduled] ) ) OVER (), 0 ) AS DECIMAL(5,1) ) AS [pctScheduledGifts]
    FROM #Gift AS [g]
    WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0
    GROUP BY DATEDIFF( DAY, '19000107', [g].[GiftDate] ) % 7
    ORDER BY DATEDIFF( DAY, '19000107', [g].[GiftDate] ) % 7
    FOR JSON PATH
);

DECLARE @JScheduledDayOfMonth NVARCHAR(MAX) = (
    SELECT
        DAY( [g].[GiftDate] ) AS [dayOfMonth],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctScheduledGifts]
    FROM #Gift AS [g]
    WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0 AND [g].[IsScheduled] = 1
    GROUP BY DAY( [g].[GiftDate] )
    ORDER BY DAY( [g].[GiftDate] )
    FOR JSON PATH
);

DECLARE @JPaymentMix NVARCHAR(MAX) = (
    SELECT
        CASE WHEN [g].[GiftDate] >= @Start12 THEN 'Last 12' ELSE 'Prior 12' END AS [period],
        ISNULL( [dv].[Value], 'None' ) AS [currencyType],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER ( PARTITION BY CASE WHEN [g].[GiftDate] >= @Start12 THEN 1 ELSE 0 END ) AS DECIMAL(5,1) ) AS [pctGifts],
        CAST( SUM( [g].[Amount] ) * 100.0 / SUM( SUM( [g].[Amount] ) ) OVER ( PARTITION BY CASE WHEN [g].[GiftDate] >= @Start12 THEN 1 ELSE 0 END ) AS DECIMAL(5,1) ) AS [pctDollars]
    FROM #Gift AS [g]
    LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [g].[CurrencyTypeValueId]
    WHERE [g].[Amount] > 0
    GROUP BY CASE WHEN [g].[GiftDate] >= @Start12 THEN 'Last 12' ELSE 'Prior 12' END,
        CASE WHEN [g].[GiftDate] >= @Start12 THEN 1 ELSE 0 END,
        [dv].[Value]
    ORDER BY 1, 3 DESC
    FOR JSON PATH
);

DECLARE @JSourceMix NVARCHAR(MAX) = (
    SELECT
        ISNULL( [dv].[Value], 'None' ) AS [sourceType],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctGifts],
        CAST( SUM( [g].[Amount] ) * 100.0 / SUM( SUM( [g].[Amount] ) ) OVER () AS DECIMAL(5,1) ) AS [pctDollars]
    FROM #Gift AS [g]
    LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [g].[SourceTypeValueId]
    WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0
    GROUP BY [dv].[Value]
    ORDER BY COUNT( * ) DESC
    FOR JSON PATH
);

DECLARE @JFundsPerGift NVARCHAR(MAX) = (
    SELECT
        [f].[Funds] AS [fundsPerGift],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctGifts]
    FROM (
        SELECT CASE WHEN [g].[AccountCount] >= 3 THEN '3+' ELSE CAST( [g].[AccountCount] AS VARCHAR(2) ) END AS [Funds]
        FROM #Gift AS [g]
        WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0
    ) AS [f]
    GROUP BY [f].[Funds]
    ORDER BY [f].[Funds]
    FOR JSON PATH
);

-- =============================================
-- Output: one row per section. Rock's SQL editor shows only this result.
-- =============================================

DECLARE @JMeta NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        1 AS [part],
        CONVERT( CHAR(10), @Start24, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT [s].[Section], [s].[Json]
FROM ( VALUES
    ( 1, 'meta', @JMeta ),
    ( 2, 'peopleSummary', @JPeopleSummary ),
    ( 3, 'ageGender', @JAgeGender ),
    ( 4, 'connectionStatus', @JConnectionStatus ),
    ( 5, 'maritalStatus', @JMaritalStatus ),
    ( 6, 'familySize', @JFamilySize ),
    ( 7, 'dataCompleteness', @JCompleteness ),
    ( 8, 'newPeopleByMonth', @JNewPeopleByMonth ),
    ( 9, 'givingSummary', @JGivingSummary ),
    ( 10, 'givingCadence', @JGivingCadence ),
    ( 11, 'givingUnitAnnualTotals', @JUnitAnnualTotals ),
    ( 12, 'givingDeciles', @JGivingDeciles ),
    ( 13, 'givingTopOnePercent', @JTopOnePercent ),
    ( 14, 'giftAmounts', @JGiftAmounts ),
    ( 15, 'givingByMonth', @JGivingByMonth ),
    ( 16, 'giftDayOfWeek', @JGiftDayOfWeek ),
    ( 17, 'scheduledGiftDayOfMonth', @JScheduledDayOfMonth ),
    ( 18, 'paymentMix', @JPaymentMix ),
    ( 19, 'sourceMix', @JSourceMix ),
    ( 20, 'fundsPerGift', @JFundsPerGift )
) AS [s] ( [Order], [Section], [Json] )
ORDER BY [s].[Order];

DROP TABLE IF EXISTS #Person, #Txn, #Gift, #GiftDay, #Unit12, #Gap, #UnitMedianGap, #UnitRhythm, #UnitModal, #UnitCadence;
