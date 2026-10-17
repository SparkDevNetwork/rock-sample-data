SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 4 of 4: where families live
--              relative to their campus.
--
--              To keep server work light, this script does no distance math.
--              It rounds each active family's geocoded home to a grid cell
--              of about 0.7 miles and returns counts per cell and campus.
--              Distances are computed later, offline, by
--              calibration/distance-from-cells.js.
--
--              PRIVACY: no names, Ids, or addresses. Homes are never
--              returned individually; only grid cells with at least
--              @MinimumFamiliesPerCell families are returned. Campuses are
--              numbered by size (1 = largest), never named. Campus
--              coordinates are exact, since campus addresses are public.
--              Keep the output in calibration/raw/ (git ignores it) and
--              delete it after normalizing.
--
--              HOW TO RUN IN ROCK:
--              1. Open Admin Tools > Settings > Power Tools > SQL Command.
--              2. Leave "Selection Query?" set to Yes. Rock runs the script in
--                 a transaction and rolls it back, so nothing can change.
--              3. Paste this whole file and click Run.
--              4. Rock shows a yellow warning that the query "modifies data."
--                 That is expected. The script only uses temp tables.
--              5. Export the grid to Excel. Expect a few thousand rows.
--
--              Also runs in SSMS. Requires SQL Server 2016 or later.
-- Date: 2026-10-15
-- =============================================

-- Settings
DECLARE @MinimumFamiliesPerCell INT = 5; -- Smaller cells are suppressed so no home can be singled out.

DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start12 DATE = DATEADD( MONTH, -12, @EndDate );

-- Lookups
DECLARE @ContributionTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '2D607262-52D6-4724-910D-5C6E8FB89ACC' );
DECLARE @PersonRecordTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' );
DECLARE @ActiveRecordStatusValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' );
DECLARE @HomeLocationTypeValueId INT = ( SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC' );

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #Campus, #Family, #GiverFamily, #AttenderFamily;

-- =============================================
-- Base sets
-- =============================================

-- Active families: the primary family of at least one active, living person.
SELECT
    [f].[FamilyId],
    [g].[CampusId],
    ROUND( [home].[GeoPoint].Lat, 2 ) AS [CellLatitude],
    ROUND( [home].[GeoPoint].Long, 2 ) AS [CellLongitude]
INTO #Family
FROM (
    SELECT DISTINCT [p].[PrimaryFamilyId] AS [FamilyId]
    FROM [Person] AS [p]
    WHERE [p].[RecordTypeValueId] = @PersonRecordTypeValueId
        AND [p].[RecordStatusValueId] = @ActiveRecordStatusValueId
        AND [p].[IsDeceased] = 0
        AND [p].[PrimaryFamilyId] IS NOT NULL
) AS [f]
INNER JOIN [Group] AS [g] ON [g].[Id] = [f].[FamilyId]
OUTER APPLY (
    SELECT TOP 1 [l].[GeoPoint]
    FROM [GroupLocation] AS [gl]
    INNER JOIN [Location] AS [l] ON [l].[Id] = [gl].[LocationId]
    WHERE [gl].[GroupId] = [f].[FamilyId]
        AND [gl].[GroupLocationTypeValueId] = @HomeLocationTypeValueId
        AND [l].[GeoPoint] IS NOT NULL
    ORDER BY [gl].[IsMappedLocation] DESC, [gl].[Id] DESC
) AS [home];

CREATE CLUSTERED INDEX [IX_FamilyId] ON #Family ( [FamilyId] );

-- Active campuses with a geocoded location, numbered by family count (1 = largest).
SELECT
    ROW_NUMBER() OVER ( ORDER BY ISNULL( [counts].[Families], 0 ) DESC, [c].[Id] ) AS [CampusNumber],
    [c].[Id] AS [CampusId],
    [l].[GeoPoint].Lat AS [Latitude],
    [l].[GeoPoint].Long AS [Longitude],
    ISNULL( [counts].[Families], 0 ) AS [Families]
INTO #Campus
FROM [Campus] AS [c]
INNER JOIN [Location] AS [l] ON [l].[Id] = [c].[LocationId]
LEFT JOIN ( SELECT [CampusId], COUNT( * ) AS [Families] FROM #Family GROUP BY [CampusId] ) AS [counts] ON [counts].[CampusId] = [c].[Id]
WHERE [c].[IsActive] = 1
    AND [l].[GeoPoint] IS NOT NULL;

-- Families with a gift in the last 12 months.
SELECT DISTINCT [gp].[PrimaryFamilyId] AS [FamilyId]
INTO #GiverFamily
FROM [FinancialTransaction] AS [ft]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
INNER JOIN [Person] AS [gp] ON [gp].[Id] = [pa].[PersonId]
WHERE [ft].[TransactionTypeValueId] = @ContributionTypeValueId
    AND [ft].[TransactionDateTime] >= @Start12
    AND [ft].[TransactionDateTime] < @EndDate
    AND [gp].[PrimaryFamilyId] IS NOT NULL;

CREATE CLUSTERED INDEX [IX_FamilyId] ON #GiverFamily ( [FamilyId] );

-- Families where anyone attended anything in the last 12 months.
SELECT DISTINCT [p].[PrimaryFamilyId] AS [FamilyId]
INTO #AttenderFamily
FROM [Attendance] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [a].[DidAttend] = 1
    AND [a].[StartDateTime] >= @Start12
    AND [a].[StartDateTime] < @EndDate
    AND [p].[PrimaryFamilyId] IS NOT NULL;

CREATE CLUSTERED INDEX [IX_FamilyId] ON #AttenderFamily ( [FamilyId] );

-- =============================================
-- Output: one result set (Rock's SQL editor shows only the first).
--   RowType 'coverage'   : how many families could be measured (Detail column).
--   RowType 'campus'     : each campus's exact location and family count.
--   RowType 'cell'       : families per grid cell and campus.
--   RowType 'suppressed' : families left out because their cell was too small.
-- =============================================

;WITH [Cells] AS (
    SELECT
        [c].[CampusNumber],
        [f].[CellLatitude],
        [f].[CellLongitude],
        COUNT( * ) AS [Families],
        SUM( CASE WHEN [at].[FamilyId] IS NOT NULL THEN 1 ELSE 0 END ) AS [AttendingFamilies],
        SUM( CASE WHEN [gv].[FamilyId] IS NOT NULL THEN 1 ELSE 0 END ) AS [GivingFamilies]
    FROM #Family AS [f]
    INNER JOIN #Campus AS [c] ON [c].[CampusId] = [f].[CampusId]
    LEFT JOIN #AttenderFamily AS [at] ON [at].[FamilyId] = [f].[FamilyId]
    LEFT JOIN #GiverFamily AS [gv] ON [gv].[FamilyId] = [f].[FamilyId]
    WHERE [f].[CellLatitude] IS NOT NULL
    GROUP BY [c].[CampusNumber], [f].[CellLatitude], [f].[CellLongitude]
)
SELECT [o].[RowType], [o].[CampusNumber], [o].[Latitude], [o].[Longitude], [o].[Families], [o].[AttendingFamilies], [o].[GivingFamilies], [o].[Detail]
FROM (
    SELECT
        1 AS [SortOrder],
        'coverage' AS [RowType],
        CAST( NULL AS INT ) AS [CampusNumber],
        CAST( NULL AS DECIMAL(9,4) ) AS [Latitude],
        CAST( NULL AS DECIMAL(9,4) ) AS [Longitude],
        ( SELECT COUNT( * ) FROM #Family ) AS [Families],
        CAST( NULL AS INT ) AS [AttendingFamilies],
        CAST( NULL AS INT ) AS [GivingFamilies],
        (
            SELECT
                ( SELECT COUNT( * ) FROM #Family ) AS [activeFamilies],
                ( SELECT COUNT( * ) FROM #Family WHERE [CampusId] IS NOT NULL ) AS [withCampus],
                ( SELECT COUNT( * ) FROM #Family WHERE [CellLatitude] IS NOT NULL ) AS [withGeocodedHome],
                ( SELECT COUNT( * ) FROM #Family AS [f] INNER JOIN #Campus AS [c] ON [c].[CampusId] = [f].[CampusId] WHERE [f].[CellLatitude] IS NOT NULL ) AS [measurable],
                @MinimumFamiliesPerCell AS [minimumFamiliesPerCell],
                CONVERT( CHAR(10), @Start12, 120 ) AS [periodStart],
                CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) AS [Detail]

    UNION ALL

    SELECT 2, 'campus', [c].[CampusNumber], CAST( [c].[Latitude] AS DECIMAL(9,4) ), CAST( [c].[Longitude] AS DECIMAL(9,4) ), [c].[Families], NULL, NULL, NULL
    FROM #Campus AS [c]

    UNION ALL

    SELECT 3, 'cell', [x].[CampusNumber], CAST( [x].[CellLatitude] AS DECIMAL(9,4) ), CAST( [x].[CellLongitude] AS DECIMAL(9,4) ), [x].[Families], [x].[AttendingFamilies], [x].[GivingFamilies], NULL
    FROM [Cells] AS [x]
    WHERE [x].[Families] >= @MinimumFamiliesPerCell

    UNION ALL

    SELECT 4, 'suppressed', NULL, NULL, NULL, ISNULL( SUM( [x].[Families] ), 0 ), ISNULL( SUM( [x].[AttendingFamilies] ), 0 ), ISNULL( SUM( [x].[GivingFamilies] ), 0 ), NULL
    FROM [Cells] AS [x]
    WHERE [x].[Families] < @MinimumFamiliesPerCell
) AS [o]
ORDER BY [o].[SortOrder], [o].[CampusNumber], [o].[Latitude], [o].[Longitude];

DROP TABLE IF EXISTS #Campus, #Family, #GiverFamily, #AttenderFamily;
