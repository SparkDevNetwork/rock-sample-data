SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Collects anonymous, aggregate statistics from a live Rock
--              database. The output calibrates the new sample data simulator
--              so its giving, attendance, groups, and engagement patterns
--              match measured patterns.
--
--              READ ONLY. Writes only to temp tables. Returns no names, no
--              Ids, and no personal data. Group type, currency type, and
--              source type names are included because they describe setup,
--              not people. Remove them before sharing if you prefer.
--
--              SSMS ONLY. On a large church this script can run longer than
--              the 180 second limit in Rock's SQL Command editor. To run
--              from inside Rock, use the three
--              Report_SampleDataCalibration_1/2/3 scripts instead.
--
--              Output: one JSON document (two result sets with the same
--              content). In SSMS, click the second result set (XML) if the
--              first one is cut off.
--
--              Requires SQL Server 2016 or later (JSON support).
-- Date: 2026-10-14
-- =============================================

-- Settings
DECLARE @ChurchLabel NVARCHAR(50) = 'Unlabeled'; -- Use a neutral label like source-2, never a church name.

-- The window is the last 24 complete months. The current month is excluded.
DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start12 DATE = DATEADD( MONTH, -12, @EndDate );
DECLARE @Start24 DATE = DATEADD( MONTH, -24, @EndDate );
DECLARE @Start26Weeks DATE = DATEADD( WEEK, -26, @EndDate );

-- Lookups
DECLARE @ContributionTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '2D607262-52D6-4724-910D-5C6E8FB89ACC' );
DECLARE @PersonRecordTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' );
DECLARE @BusinessRecordTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = 'BF64ADD3-E70A-44CE-9C4B-E76BBED37550' );
DECLARE @ActiveRecordStatusValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' );
DECLARE @HomeLocationTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC' );
DECLARE @MobilePhoneTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '407E7E45-7B2E-4FCD-9605-ECB1339F2453' );

-- Clean up temp tables from an earlier run in the same session.
DROP TABLE IF EXISTS #ExcludedGroupType, #Person, #Txn, #Gift, #GiftDay, #Unit12, #Gap, #UnitMedianGap, #UnitRhythm,
    #UnitModal, #UnitCadence, #Att, #ActiveMember, #GroupSize, #GroupTypeMedian, #Reg, #CommId, #Comm, #RecipientAgg, #ConnReq;

-- Group types that are not real "groups" (families, security roles, relationships).
CREATE TABLE #ExcludedGroupType ( [Id] INT PRIMARY KEY );
INSERT INTO #ExcludedGroupType ( [Id] )
SELECT [gt].[Id]
FROM [GroupType] AS [gt]
WHERE [gt].[Guid] IN (
    '790E3215-3B10-442B-AF69-616C0DCB998E', -- Family
    'AECE949F-704C-483E-A4FB-93D5E4720C4C', -- Security Role
    'E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF', -- Known Relationships
    '8C0E5852-F08F-4327-9AA5-87800A6AB53E'  -- Peer Network
);

-- =============================================
-- Base sets
-- =============================================

-- Active, living people (not businesses).
SELECT
    [p].[Id] AS [PersonId],
    [p].[GivingId],
    [p].[PrimaryFamilyId],
    [p].[PrimaryCampusId],
    [p].[AgeClassification],
    [p].[Gender],
    [p].[BirthDate],
    CAST( FLOOR( DATEDIFF( DAY, [p].[BirthDate], @EndDate ) / 365.25 ) AS INT ) AS [Age],
    [p].[GraduationYear],
    [p].[ConnectionStatusValueId],
    [p].[MaritalStatusValueId],
    [p].[CreatedDateTime],
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
    [t].[TransactionDateTime] AS [GiftDateTime],
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

-- Days between giving events, per unit.
SELECT
    [gd].[GivingId],
    DATEDIFF( DAY, LAG( [gd].[GiftDate] ) OVER ( PARTITION BY [gd].[GivingId] ORDER BY [gd].[GiftDate] ), [gd].[GiftDate] ) AS [Gap]
INTO #Gap
FROM #GiftDay AS [gd]
WHERE [gd].[GiftDate] >= @Start12;

DELETE FROM #Gap WHERE [Gap] IS NULL;

SELECT DISTINCT
    [g].[GivingId],
    PERCENTILE_CONT( 0.5 ) WITHIN GROUP ( ORDER BY [g].[Gap] ) OVER ( PARTITION BY [g].[GivingId] ) AS [MedianGap]
INTO #UnitMedianGap
FROM #Gap AS [g];

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

-- Attendance in the 24 month window, including did-not-attend rows.
-- A record with a device or attendance code came from check-in.
SELECT
    [pa].[PersonId],
    [p].[AgeClassification],
    [p].[PrimaryFamilyId],
    [ao].[SundayDate],
    [a].[StartDateTime],
    [g].[GroupTypeId],
    [a].[DidAttend],
    CASE WHEN [a].[DeviceId] IS NOT NULL OR [a].[AttendanceCodeId] IS NOT NULL THEN 1 ELSE 0 END AS [IsCheckIn],
    CASE WHEN [a].[IsFirstTime] = 1 THEN 1 ELSE 0 END AS [IsFirstTime]
INTO #Att
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [ao] ON [ao].[Id] = [a].[OccurrenceId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
LEFT JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId]
WHERE [a].[StartDateTime] >= @Start24
    AND [a].[StartDateTime] < @EndDate;

CREATE CLUSTERED INDEX [IX_PersonId] ON #Att ( [PersonId] );

-- Active members of active, real groups.
SELECT
    [gm].[GroupId],
    [gm].[GroupTypeId],
    [gm].[PersonId],
    CASE WHEN [gtr].[IsLeader] = 1 THEN 1 ELSE 0 END AS [IsLeader]
INTO #ActiveMember
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [gtr] ON [gtr].[Id] = [gm].[GroupRoleId]
WHERE [gm].[GroupMemberStatus] = 1
    AND [gm].[IsArchived] = 0
    AND [g].[IsActive] = 1
    AND [g].[IsArchived] = 0
    AND [gm].[GroupTypeId] NOT IN ( SELECT [Id] FROM #ExcludedGroupType );

CREATE CLUSTERED INDEX [IX_PersonId] ON #ActiveMember ( [PersonId] );

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

DECLARE @JUnitAnnualTotals NVARCHAR(MAX) = (
    SELECT TOP 1
        CAST( PERCENTILE_CONT( 0.10 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p10],
        CAST( PERCENTILE_CONT( 0.25 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p25],
        CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p50],
        CAST( PERCENTILE_CONT( 0.75 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p75],
        CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p90],
        CAST( PERCENTILE_CONT( 0.95 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p95],
        CAST( PERCENTILE_CONT( 0.99 ) WITHIN GROUP ( ORDER BY [u].[Total] ) OVER () AS DECIMAL(12,2) ) AS [p99]
    FROM #Unit12 AS [u]
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
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.10 ) WITHIN GROUP ( ORDER BY [g].[Amount] ) OVER () AS DECIMAL(12,2) ) AS [p10],
            CAST( PERCENTILE_CONT( 0.25 ) WITHIN GROUP ( ORDER BY [g].[Amount] ) OVER () AS DECIMAL(12,2) ) AS [p25],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [g].[Amount] ) OVER () AS DECIMAL(12,2) ) AS [p50],
            CAST( PERCENTILE_CONT( 0.75 ) WITHIN GROUP ( ORDER BY [g].[Amount] ) OVER () AS DECIMAL(12,2) ) AS [p75],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [g].[Amount] ) OVER () AS DECIMAL(12,2) ) AS [p90],
            CAST( PERCENTILE_CONT( 0.99 ) WITHIN GROUP ( ORDER BY [g].[Amount] ) OVER () AS DECIMAL(12,2) ) AS [p99]
        FROM #Gift AS [g]
        WHERE [g].[GiftDate] >= @Start12 AND [g].[Amount] > 0
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
-- Attendance and check-in
-- =============================================

DECLARE @JAttendanceWeekly NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(10), [a].[SundayDate], 120 ) AS [weekEnding],
        COUNT( DISTINCT CASE WHEN [a].[IsCheckIn] = 1 AND [a].[AgeClassification] = 2 THEN [a].[PersonId] END ) AS [checkInChildren],
        COUNT( DISTINCT CASE WHEN [a].[IsCheckIn] = 1 AND [a].[AgeClassification] <> 2 THEN [a].[PersonId] END ) AS [checkInAdults],
        COUNT( DISTINCT CASE WHEN [a].[IsCheckIn] = 0 THEN [a].[PersonId] END ) AS [nonCheckInAttendees],
        SUM( CASE WHEN [a].[IsCheckIn] = 1 THEN [a].[IsFirstTime] ELSE 0 END ) AS [firstTimeCheckIns]
    FROM #Att AS [a]
    WHERE [a].[DidAttend] = 1
    GROUP BY [a].[SundayDate]
    ORDER BY [a].[SundayDate]
    FOR JSON PATH
);

-- How many of the last 26 weeks each checked-in person attended.
DECLARE @JCheckInFrequency NVARCHAR(MAX) = (
    SELECT
        [w].[AgeGroup] AS [ageGroup],
        [w].[WeeksBucket] AS [weeksAttendedOf26],
        COUNT( * ) AS [people]
    FROM (
        SELECT
            CASE WHEN MAX( [a].[AgeClassification] ) = 2 THEN 'Child' ELSE 'Adult or unknown' END AS [AgeGroup],
            CASE
                WHEN COUNT( DISTINCT [a].[SundayDate] ) = 1 THEN '01'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 3 THEN '02-03'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 7 THEN '04-07'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 12 THEN '08-12'
                WHEN COUNT( DISTINCT [a].[SundayDate] ) <= 19 THEN '13-19'
                ELSE '20-26'
            END AS [WeeksBucket]
        FROM #Att AS [a]
        WHERE [a].[DidAttend] = 1
            AND [a].[IsCheckIn] = 1
            AND [a].[StartDateTime] >= @Start26Weeks
        GROUP BY [a].[PersonId]
    ) AS [w]
    GROUP BY [w].[AgeGroup], [w].[WeeksBucket]
    ORDER BY [w].[AgeGroup], [w].[WeeksBucket]
    FOR JSON PATH
);

-- Check-in times (0 = Sunday). Only slots with at least 0.1% of check-ins.
DECLARE @JCheckInTimes NVARCHAR(MAX) = (
    SELECT [t].*
    FROM (
        SELECT
            DATEDIFF( DAY, '19000107', [a].[StartDateTime] ) % 7 AS [dayOfWeek],
            DATEPART( HOUR, [a].[StartDateTime] ) AS [hour],
            CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctCheckIns]
        FROM #Att AS [a]
        WHERE [a].[DidAttend] = 1 AND [a].[IsCheckIn] = 1 AND [a].[StartDateTime] >= @Start12
        GROUP BY DATEDIFF( DAY, '19000107', [a].[StartDateTime] ) % 7, DATEPART( HOUR, [a].[StartDateTime] )
    ) AS [t]
    WHERE [t].[pctCheckIns] >= 0.1
    ORDER BY [t].[dayOfWeek], [t].[hour]
    FOR JSON PATH
);

-- When a family checks kids in, how many kids come together.
DECLARE @JKidsPerFamilyCheckIn NVARCHAR(MAX) = (
    SELECT
        [k].[Kids] AS [kidsCheckedIn],
        CAST( COUNT( * ) * 100.0 / SUM( COUNT( * ) ) OVER () AS DECIMAL(5,1) ) AS [pctFamilyWeeks]
    FROM (
        SELECT CASE WHEN COUNT( DISTINCT [a].[PersonId] ) >= 4 THEN '4+' ELSE CAST( COUNT( DISTINCT [a].[PersonId] ) AS VARCHAR(2) ) END AS [Kids]
        FROM #Att AS [a]
        WHERE [a].[DidAttend] = 1
            AND [a].[IsCheckIn] = 1
            AND [a].[AgeClassification] = 2
            AND [a].[PrimaryFamilyId] IS NOT NULL
            AND [a].[StartDateTime] >= @Start12
        GROUP BY [a].[PrimaryFamilyId], [a].[SundayDate]
    ) AS [k]
    GROUP BY [k].[Kids]
    ORDER BY [k].[Kids]
    FOR JSON PATH
);

-- =============================================
-- Groups and serving
-- =============================================

SELECT [am].[GroupId], [am].[GroupTypeId], COUNT( * ) AS [Members]
INTO #GroupSize
FROM #ActiveMember AS [am]
GROUP BY [am].[GroupId], [am].[GroupTypeId];

SELECT DISTINCT
    [gs].[GroupTypeId],
    PERCENTILE_CONT( 0.5 ) WITHIN GROUP ( ORDER BY [gs].[Members] ) OVER ( PARTITION BY [gs].[GroupTypeId] ) AS [MedianGroupSize]
INTO #GroupTypeMedian
FROM #GroupSize AS [gs];

DECLARE @JGroupTypes NVARCHAR(MAX) = (
    SELECT TOP 30
        [gt].[Name] AS [groupType],
        ISNULL( [pv].[Value], '' ) AS [purpose],
        [gt].[TakesAttendance] AS [takesAttendance],
        [gt].[IsSchedulingEnabled] AS [isSchedulingEnabled],
        [s].[ActiveGroups] AS [activeGroups],
        [s].[ActiveMembers] AS [activeMembers],
        [s].[DistinctPeople] AS [distinctPeople],
        [s].[PctLeaders] AS [pctLeaders],
        CAST( [m].[MedianGroupSize] AS DECIMAL(6,1) ) AS [medianGroupSize],
        ISNULL( [ar].[Records], 0 ) AS [attendanceRecordsLast12],
        [ar].[PctDidAttend] AS [pctDidAttend]
    FROM [GroupType] AS [gt]
    INNER JOIN (
        SELECT
            [am].[GroupTypeId],
            COUNT( DISTINCT [am].[GroupId] ) AS [ActiveGroups],
            COUNT( * ) AS [ActiveMembers],
            COUNT( DISTINCT [am].[PersonId] ) AS [DistinctPeople],
            CAST( AVG( [am].[IsLeader] * 100.0 ) AS DECIMAL(5,1) ) AS [PctLeaders]
        FROM #ActiveMember AS [am]
        GROUP BY [am].[GroupTypeId]
    ) AS [s] ON [s].[GroupTypeId] = [gt].[Id]
    LEFT JOIN #GroupTypeMedian AS [m] ON [m].[GroupTypeId] = [gt].[Id]
    LEFT JOIN [DefinedValue] AS [pv] ON [pv].[Id] = [gt].[GroupTypePurposeValueId]
    LEFT JOIN (
        SELECT
            [a].[GroupTypeId],
            COUNT( * ) AS [Records],
            CAST( AVG( CASE WHEN [a].[DidAttend] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [PctDidAttend]
        FROM #Att AS [a]
        WHERE [a].[StartDateTime] >= @Start12 AND [a].[DidAttend] IS NOT NULL
        GROUP BY [a].[GroupTypeId]
    ) AS [ar] ON [ar].[GroupTypeId] = [gt].[Id]
    ORDER BY [s].[ActiveMembers] DESC
    FOR JSON PATH
);

DECLARE @JGroupsPerAdult NVARCHAR(MAX) = (
    SELECT
        [c].[Groups] AS [activeGroups],
        COUNT( * ) AS [adults]
    FROM (
        SELECT
            CASE
                WHEN COUNT( DISTINCT [am].[GroupId] ) >= 3 THEN '3+'
                ELSE CAST( COUNT( DISTINCT [am].[GroupId] ) AS VARCHAR(2) )
            END AS [Groups]
        FROM #Person AS [p]
        LEFT JOIN #ActiveMember AS [am] ON [am].[PersonId] = [p].[PersonId]
        WHERE [p].[AgeClassification] = 1
        GROUP BY [p].[PersonId]
    ) AS [c]
    GROUP BY [c].[Groups]
    ORDER BY [c].[Groups]
    FOR JSON PATH
);

-- =============================================
-- Cross-feature engagement (active adults, last 12 months)
-- This is the most important section: it shows how features overlap.
-- =============================================

DECLARE @JEngagement NVARCHAR(MAX) = (
    SELECT
        [e].[ConnectionStatus] AS [connectionStatus],
        [e].[Gives] AS [gives],
        [e].[Attends] AS [attends],
        [e].[InGroup] AS [inGroup],
        [e].[Serves] AS [serves],
        COUNT( * ) AS [adults]
    FROM (
        SELECT
            ISNULL( [dv].[Value], 'None' ) AS [ConnectionStatus],
            CASE WHEN EXISTS ( SELECT 1 FROM #Unit12 AS [u] WHERE [u].[GivingId] = [p].[GivingId] ) THEN 1 ELSE 0 END AS [Gives],
            CASE WHEN EXISTS ( SELECT 1 FROM #Att AS [a] WHERE [a].[PersonId] = [p].[PersonId] AND [a].[DidAttend] = 1 AND [a].[StartDateTime] >= @Start12 ) THEN 1 ELSE 0 END AS [Attends],
            CASE WHEN EXISTS (
                SELECT 1
                FROM #ActiveMember AS [am]
                INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [am].[GroupTypeId]
                WHERE [am].[PersonId] = [p].[PersonId] AND [gt].[IsSchedulingEnabled] = 0 AND [gt].[TakesAttendance] = 1
            ) THEN 1 ELSE 0 END AS [InGroup],
            CASE WHEN EXISTS (
                SELECT 1
                FROM #ActiveMember AS [am]
                INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [am].[GroupTypeId]
                WHERE [am].[PersonId] = [p].[PersonId] AND [gt].[IsSchedulingEnabled] = 1
            ) THEN 1 ELSE 0 END AS [Serves]
        FROM #Person AS [p]
        LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [p].[ConnectionStatusValueId]
        WHERE [p].[AgeClassification] = 1
    ) AS [e]
    GROUP BY [e].[ConnectionStatus], [e].[Gives], [e].[Attends], [e].[InGroup], [e].[Serves]
    ORDER BY [e].[ConnectionStatus], [e].[Gives] DESC, [e].[Attends] DESC, [e].[InGroup] DESC, [e].[Serves] DESC
    FOR JSON PATH
);

-- Adults added 13-24 months ago: how many are still active, and where are they now?
DECLARE @JNewPeopleOutcome NVARCHAR(MAX) = (
    SELECT
        ( SELECT COUNT( * ) FROM [Person] AS [ap] WHERE [ap].[RecordTypeValueId] = @PersonRecordTypeValueId AND [ap].[AgeClassification] = 1 AND [ap].[CreatedDateTime] >= @Start24 AND [ap].[CreatedDateTime] < @Start12 ) AS [adultsAdded],
        COUNT( * ) AS [stillActive],
        CAST( AVG( [n].[Gave] * 100.0 ) AS DECIMAL(5,1) ) AS [pctGaveLast12],
        CAST( AVG( [n].[Attended] * 100.0 ) AS DECIMAL(5,1) ) AS [pctAttendedLast12],
        CAST( AVG( [n].[InAnyGroup] * 100.0 ) AS DECIMAL(5,1) ) AS [pctInAnyGroup]
    FROM (
        SELECT
            CASE WHEN EXISTS ( SELECT 1 FROM #Unit12 AS [u] WHERE [u].[GivingId] = [p].[GivingId] ) THEN 1 ELSE 0 END AS [Gave],
            CASE WHEN EXISTS ( SELECT 1 FROM #Att AS [a] WHERE [a].[PersonId] = [p].[PersonId] AND [a].[DidAttend] = 1 AND [a].[StartDateTime] >= @Start12 ) THEN 1 ELSE 0 END AS [Attended],
            CASE WHEN EXISTS ( SELECT 1 FROM #ActiveMember AS [am] WHERE [am].[PersonId] = [p].[PersonId] ) THEN 1 ELSE 0 END AS [InAnyGroup]
        FROM #Person AS [p]
        WHERE [p].[AgeClassification] = 1
            AND [p].[CreatedDateTime] >= @Start24
            AND [p].[CreatedDateTime] < @Start12
    ) AS [n]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Registrations (last 12 months)
-- =============================================

SELECT [r].[Id] AS [RegistrationId], [r].[RegistrationInstanceId]
INTO #Reg
FROM [Registration] AS [r]
WHERE [r].[IsTemporary] = 0
    AND [r].[CreatedDateTime] >= @Start12
    AND [r].[CreatedDateTime] < @EndDate;

DECLARE @JRegistrations NVARCHAR(MAX) = (
    SELECT [s].*, [pc].*
    FROM (
        SELECT
            COUNT( DISTINCT [r].[RegistrationInstanceId] ) AS [instancesWithRegistrations],
            COUNT( DISTINCT [r].[RegistrationId] ) AS [registrations],
            COUNT( [rr].[Id] ) AS [registrants],
            CAST( COUNT( [rr].[Id] ) * 1.0 / NULLIF( COUNT( DISTINCT [r].[RegistrationId] ), 0 ) AS DECIMAL(5,2) ) AS [avgRegistrantsPerRegistration],
            CAST( AVG( CASE WHEN [rr].[OnWaitList] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWaitList],
            CAST( AVG( CASE WHEN [rr].[Cost] > 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPaidRegistrants],
            CAST( AVG( CASE WHEN [p].[AgeClassification] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctChildRegistrants]
        FROM #Reg AS [r]
        LEFT JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[RegistrationId]
        LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [rr].[PersonAliasId]
        LEFT JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.25 ) WITHIN GROUP ( ORDER BY [ic].[Registrants] ) OVER () AS [registrantsPerInstanceP25],
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [ic].[Registrants] ) OVER () AS [registrantsPerInstanceP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [ic].[Registrants] ) OVER () AS [registrantsPerInstanceP90]
        FROM (
            SELECT [r].[RegistrationInstanceId], COUNT( [rr].[Id] ) AS [Registrants]
            FROM #Reg AS [r]
            LEFT JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[RegistrationId]
            GROUP BY [r].[RegistrationInstanceId]
        ) AS [ic]
    ) AS [pc]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Prayer (last 24 months)
-- =============================================

DECLARE @JPrayerSummary NVARCHAR(MAX) = (
    SELECT
        COUNT( * ) AS [requests],
        CAST( AVG( CASE WHEN [pr].[IsApproved] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctApproved],
        CAST( AVG( CASE WHEN [pr].[IsPublic] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPublic],
        CAST( AVG( CASE WHEN [pr].[IsUrgent] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUrgent],
        CAST( AVG( CASE WHEN [pr].[AllowComments] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAllowComments],
        CAST( AVG( CASE WHEN ISNULL( [pr].[Answer], '' ) <> '' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAnswered],
        CAST( AVG( CASE WHEN [pr].[RequestedByPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctLinkedToPerson],
        CAST( AVG( CASE WHEN [pr].[CategoryId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCategory],
        CAST( AVG( ISNULL( [pr].[PrayerCount], 0 ) * 1.0 ) AS DECIMAL(6,1) ) AS [avgPrayerCount],
        CAST( AVG( LEN( [pr].[Text] ) * 1.0 ) AS DECIMAL(7,0) ) AS [avgTextLength]
    FROM [PrayerRequest] AS [pr]
    WHERE [pr].[EnteredDateTime] >= @Start24 AND [pr].[EnteredDateTime] < @EndDate
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JPrayerByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [pr].[EnteredDateTime], 120 ) AS [month],
        COUNT( * ) AS [requests]
    FROM [PrayerRequest] AS [pr]
    WHERE [pr].[EnteredDateTime] >= @Start24 AND [pr].[EnteredDateTime] < @EndDate
    GROUP BY CONVERT( CHAR(7), [pr].[EnteredDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [pr].[EnteredDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Communications (last 12 months, sent only)
-- =============================================

-- Communication rows hold the full message body, so read the Ids from the
-- SendDateTime index first, then fetch only those rows by primary key.
SELECT [c].[Id] AS [CommunicationId]
INTO #CommId
FROM [Communication] AS [c]
WHERE [c].[SendDateTime] >= @Start12 AND [c].[SendDateTime] < @EndDate;

-- Communication type: 0 Recipient preference, 1 Email, 2 SMS, 3 Push.
SELECT
    [c].[Id] AS [CommunicationId],
    [c].[IsBulkCommunication],
    [c].[SendDateTime],
    CASE [c].[CommunicationType] WHEN 1 THEN 'Email' WHEN 2 THEN 'SMS' WHEN 3 THEN 'Push' ELSE 'Recipient preference' END AS [Medium]
INTO #Comm
FROM #CommId AS [ci]
INNER JOIN [Communication] AS [c] WITH ( FORCESEEK ) ON [c].[Id] = [ci].[CommunicationId];

CREATE CLUSTERED INDEX [IX_CommunicationId] ON #Comm ( [CommunicationId] );

/*
    10/14/26 - CLAUDE

    Each CommunicationRecipient row stores a copy of the sent message, so this
    is often the largest table in the database. This query uses only columns
    in the IX_CommunicationId index (CommunicationId, Status, OpenedDateTime)
    so SQL never reads the full rows. The medium comes from the communication,
    not the recipient, for the same reason. Only the last 3 months are read.

    Reason: Reading full recipient rows timed out on a large church.
*/
-- Recipient status: 0 Pending, 1 Delivered, 2 Failed, 3 Cancelled, 4 Opened, 5 Sending.
SELECT
    [c].[CommunicationId],
    [c].[Medium],
    COUNT( * ) AS [Recipients],
    SUM( CASE WHEN [cr].[Status] IN ( 1, 4 ) THEN 1 ELSE 0 END ) AS [Delivered],
    SUM( CASE WHEN [cr].[Status] = 4 OR [cr].[OpenedDateTime] IS NOT NULL THEN 1 ELSE 0 END ) AS [Opened],
    SUM( CASE WHEN [cr].[Status] = 2 THEN 1 ELSE 0 END ) AS [Failed],
    SUM( CASE WHEN [cr].[Status] = 3 THEN 1 ELSE 0 END ) AS [Cancelled]
INTO #RecipientAgg
FROM #Comm AS [c]
INNER JOIN [CommunicationRecipient] AS [cr] ON [cr].[CommunicationId] = [c].[CommunicationId]
WHERE [c].[SendDateTime] >= DATEADD( MONTH, -3, @EndDate )
GROUP BY [c].[CommunicationId], [c].[Medium];

DECLARE @JCommunicationsByMedium NVARCHAR(MAX) = (
    SELECT
        [ra].[Medium] AS [medium],
        COUNT( DISTINCT [ra].[CommunicationId] ) AS [communications],
        SUM( [ra].[Recipients] ) AS [recipients],
        CAST( SUM( [ra].[Delivered] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctDelivered],
        CAST( SUM( [ra].[Opened] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctOpened],
        CAST( SUM( [ra].[Failed] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctFailed],
        CAST( SUM( [ra].[Cancelled] ) * 100.0 / NULLIF( SUM( [ra].[Recipients] ), 0 ) AS DECIMAL(5,1) ) AS [pctCancelled]
    FROM #RecipientAgg AS [ra]
    GROUP BY [ra].[Medium]
    ORDER BY SUM( [ra].[Recipients] ) DESC
    FOR JSON PATH
);

DECLARE @JCommunicationSize NVARCHAR(MAX) = (
    SELECT [s].*, [pc].*
    FROM (
        SELECT
            COUNT( * ) AS [communicationsSent],
            CAST( AVG( CASE WHEN [c].[IsBulkCommunication] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctBulk]
        FROM #Comm AS [c]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [t].[Recipients] ) OVER () AS [recipientsP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [t].[Recipients] ) OVER () AS [recipientsP90],
            PERCENTILE_CONT( 0.99 ) WITHIN GROUP ( ORDER BY [t].[Recipients] ) OVER () AS [recipientsP99]
        FROM (
            SELECT [ra].[CommunicationId], SUM( [ra].[Recipients] ) AS [Recipients]
            FROM #RecipientAgg AS [ra]
            GROUP BY [ra].[CommunicationId]
        ) AS [t]
    ) AS [pc]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JCommunicationsByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [c].[SendDateTime], 120 ) AS [month],
        COUNT( * ) AS [communications]
    FROM #Comm AS [c]
    GROUP BY CONVERT( CHAR(7), [c].[SendDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [c].[SendDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Connections (last 24 months)
-- =============================================

SELECT
    [cr].[ConnectionOpportunityId],
    [cr].[ConnectionState],
    [cr].[ConnectorPersonAliasId],
    [cr].[CampusId],
    [cr].[CreatedDateTime],
    [cr].[ConnectedDateTime]
INTO #ConnReq
FROM [ConnectionRequest] AS [cr]
WHERE [cr].[CreatedDateTime] >= @Start24 AND [cr].[CreatedDateTime] < @EndDate;

-- Connection state: 0 Active, 1 Inactive, 2 Future Follow Up, 3 Connected.
DECLARE @JConnections NVARCHAR(MAX) = (
    SELECT [s].*, [ct].*, [op].*
    FROM (
        SELECT
            COUNT( * ) AS [requests],
            COUNT( DISTINCT [c].[ConnectionOpportunityId] ) AS [opportunitiesWithRequests],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctActive],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctInactive],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctFutureFollowUp],
            CAST( AVG( CASE WHEN [c].[ConnectionState] = 3 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctConnected],
            CAST( AVG( CASE WHEN [c].[ConnectorPersonAliasId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithConnector],
            CAST( AVG( CASE WHEN [c].[CampusId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCampus]
        FROM #ConnReq AS [c]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [c].[CreatedDateTime], [c].[ConnectedDateTime] ) ) OVER () AS [daysToConnectP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY DATEDIFF( DAY, [c].[CreatedDateTime], [c].[ConnectedDateTime] ) ) OVER () AS [daysToConnectP90]
        FROM #ConnReq AS [c]
        WHERE [c].[ConnectedDateTime] IS NOT NULL
    ) AS [ct]
    OUTER APPLY (
        SELECT TOP 1
            PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [o].[Requests] ) OVER () AS [requestsPerOpportunityP50],
            PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [o].[Requests] ) OVER () AS [requestsPerOpportunityP90]
        FROM (
            SELECT [c].[ConnectionOpportunityId], COUNT( * ) AS [Requests]
            FROM #ConnReq AS [c]
            GROUP BY [c].[ConnectionOpportunityId]
        ) AS [o]
    ) AS [op]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JConnectionsByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [c].[CreatedDateTime], 120 ) AS [month],
        COUNT( * ) AS [requests]
    FROM #ConnReq AS [c]
    GROUP BY CONVERT( CHAR(7), [c].[CreatedDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [c].[CreatedDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Combine into one JSON document
-- =============================================

DECLARE @Result NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        CONVERT( CHAR(10), @Start24, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive],
        JSON_QUERY( @JPeopleSummary ) AS [peopleSummary],
        JSON_QUERY( @JAgeGender ) AS [ageGender],
        JSON_QUERY( @JConnectionStatus ) AS [connectionStatus],
        JSON_QUERY( @JMaritalStatus ) AS [maritalStatus],
        JSON_QUERY( @JFamilySize ) AS [familySize],
        JSON_QUERY( @JCompleteness ) AS [dataCompleteness],
        JSON_QUERY( @JNewPeopleByMonth ) AS [newPeopleByMonth],
        JSON_QUERY( @JNewPeopleOutcome ) AS [newPeopleOutcome],
        JSON_QUERY( @JGivingSummary ) AS [givingSummary],
        JSON_QUERY( @JGivingCadence ) AS [givingCadence],
        JSON_QUERY( @JUnitAnnualTotals ) AS [givingUnitAnnualTotals],
        JSON_QUERY( @JGivingDeciles ) AS [givingDeciles],
        JSON_QUERY( @JTopOnePercent ) AS [givingTopOnePercent],
        JSON_QUERY( @JGiftAmounts ) AS [giftAmounts],
        JSON_QUERY( @JGivingByMonth ) AS [givingByMonth],
        JSON_QUERY( @JGiftDayOfWeek ) AS [giftDayOfWeek],
        JSON_QUERY( @JScheduledDayOfMonth ) AS [scheduledGiftDayOfMonth],
        JSON_QUERY( @JPaymentMix ) AS [paymentMix],
        JSON_QUERY( @JSourceMix ) AS [sourceMix],
        JSON_QUERY( @JFundsPerGift ) AS [fundsPerGift],
        JSON_QUERY( @JAttendanceWeekly ) AS [attendanceWeekly],
        JSON_QUERY( @JCheckInFrequency ) AS [checkInFrequency],
        JSON_QUERY( @JCheckInTimes ) AS [checkInTimes],
        JSON_QUERY( @JKidsPerFamilyCheckIn ) AS [kidsPerFamilyCheckIn],
        JSON_QUERY( @JGroupTypes ) AS [groupTypes],
        JSON_QUERY( @JGroupsPerAdult ) AS [groupsPerAdult],
        JSON_QUERY( @JEngagement ) AS [engagement],
        JSON_QUERY( @JRegistrations ) AS [registrations],
        JSON_QUERY( @JPrayerSummary ) AS [prayerSummary],
        JSON_QUERY( @JPrayerByMonth ) AS [prayerByMonth],
        JSON_QUERY( @JCommunicationSize ) AS [communicationSummaryRecipientsLast3Months],
        JSON_QUERY( @JCommunicationsByMedium ) AS [communicationsByMediumLast3Months],
        JSON_QUERY( @JCommunicationsByMonth ) AS [communicationsByMonth],
        JSON_QUERY( @JConnections ) AS [connections],
        JSON_QUERY( @JConnectionsByMonth ) AS [connectionsByMonth]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
);

-- Result 1: plain text. Result 2: same content, clickable in SSMS with no length limit.
SELECT @Result AS [CalibrationJson];
SELECT @Result AS [processing-instruction(CalibrationJson)] FOR XML PATH( '' ), TYPE;

DROP TABLE IF EXISTS #ExcludedGroupType, #Person, #Txn, #Gift, #GiftDay, #Unit12, #Gap, #UnitMedianGap, #UnitRhythm,
    #UnitModal, #UnitCadence, #Att, #ActiveMember, #GroupSize, #GroupTypeMedian, #Reg, #CommId, #Comm, #RecipientAgg, #ConnReq;
