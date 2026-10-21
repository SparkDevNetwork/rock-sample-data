SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- =============================================
-- Description: Sample data calibration, part 6: notes on person records.
--
--              Measures how staff use notes on people: which kinds of note
--              types they use, how long notes are, how often notes are
--              alerts, private, pinned, or replies, when notes are written,
--              who writes them, and who they are about.
--
--              Note type names are never returned. The script sorts each
--              note type into a generic kind (care, hospital, counseling,
--              follow-up, and so on) from Rock's own Guids and from words in
--              the name, and returns only the kind.
--
--              Every per-note measure stands alone. Run part 2 on the same
--              database only if you also want yearly volumes, which are
--              scaled by part 2's active adult count.
--
--              Collects anonymous, aggregate statistics from a live Rock
--              database. Returns no names, no Ids, no note type names, and
--              no note text.
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
--              Use the same @ChurchLabel as in part 2.
--              Also runs in SSMS. Requires SQL Server 2016 or later.
-- Date: 2026-10-20
-- =============================================

-- Settings
DECLARE @ChurchLabel NVARCHAR(50) = 'Unlabeled'; -- Use a neutral label like source-2, never a church name.

-- The window is the last 24 complete months. The current month is excluded.
DECLARE @EndDate DATE = DATEFROMPARTS( YEAR( GETDATE() ), MONTH( GETDATE() ), 1 );
DECLARE @Start24 DATE = DATEADD( MONTH, -24, @EndDate );

-- Lookups
DECLARE @PersonEntityTypeId INT = ( SELECT TOP 1 [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' );
DECLARE @NoteTypeEntityTypeId INT = ( SELECT TOP 1 [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.NoteType' );

-- Clean up temp tables from an earlier run in the same SSMS session.
DROP TABLE IF EXISTS #Type, #Staff, #Note, #Author;

-- =============================================
-- Base sets
-- =============================================

/*
    10/20/26 - CLAUDE

    Note type names often carry a ministry, campus, or staff name, so they
    must never leave the database. Each type is sorted into a generic kind
    here, and only the kind is returned. Rock's own types are matched by
    Guid first. Custom types are matched by words in the name, in order, so
    "Hospital Care Visit" counts as hospital, not care.

    Reason: Learn which kinds of notes churches keep without exposing names.
*/
SELECT
    [nt].[Id] AS [NoteTypeId],
    CASE
        WHEN [nt].[Guid] = '66A1B9D7-7EFA-40F3-9415-E54437977D60' THEN 'personal'
        WHEN [nt].[Guid] = '5B7BE8F4-481B-4BA3-99A2-B26307A2BA42' THEN 'pastoral'
        WHEN [nt].[Guid] = 'A3F5982F-C4D0-4345-8021-EB38C4C9AA18' THEN 'quick'
        WHEN [nt].[Guid] = 'BBADA8EF-23FC-4B46-B7A7-0F6D31F8C045' THEN 'registration'
        WHEN [nt].[Guid] = '4E22E9DA-06A7-45DB-9EB3-BFCB7A2A7F21' THEN 'importError'
        WHEN [nt].[Guid] = 'A480E95A-F941-473E-B200-50D2658C9417' THEN 'celebration'
        WHEN [nt].[Guid] = '87BACB34-DB87-45E0-AB60-BFABF7CEECDB' THEN 'communication'
        WHEN [nt].[Guid] = 'B54F9D90-9AF3-4E8A-8F33-9338C7C1287F' THEN 'phone'
        WHEN [nt].[Name] LIKE '%hospital%' THEN 'hospital'
        WHEN [nt].[Name] LIKE '%counsel%' THEN 'counseling'
        WHEN [nt].[Name] LIKE '%benevol%' OR [nt].[Name] LIKE '%assistance%' OR [nt].[Name] LIKE '%financ%' THEN 'benevolence'
        WHEN [nt].[Name] LIKE '%alert%' OR [nt].[Name] LIKE '%security%' OR [nt].[Name] LIKE '%safety%' OR [nt].[Name] LIKE '%restrict%' OR [nt].[Name] LIKE '%caution%' OR [nt].[Name] LIKE '%concern%' THEN 'safety'
        WHEN [nt].[Name] LIKE '%pastor%' OR [nt].[Name] LIKE '%care%' OR [nt].[Name] LIKE '%visit%' OR [nt].[Name] LIKE '%shepherd%' OR [nt].[Name] LIKE '%elder%' THEN 'care'
        WHEN [nt].[Name] LIKE '%follow%' THEN 'followUp'
        WHEN [nt].[Name] LIKE '%phone%' OR [nt].[Name] LIKE '%call%' THEN 'phone'
        WHEN [nt].[Name] LIKE '%email%' OR [nt].[Name] LIKE '%sms%' OR [nt].[Name] LIKE '%text%' OR [nt].[Name] LIKE '%communicat%' THEN 'communication'
        WHEN [nt].[Name] LIKE '%member%' THEN 'membership'
        WHEN [nt].[Name] LIKE '%prayer%' THEN 'prayer'
        WHEN [nt].[Name] LIKE '%baptis%' OR [nt].[Name] LIKE '%salvation%' OR [nt].[Name] LIKE '%decision%' THEN 'faithMilestone'
        WHEN [nt].[Name] LIKE '%volunteer%' OR [nt].[Name] LIKE '%serv%' OR [nt].[Name] LIKE '%background%' THEN 'serving'
        WHEN [nt].[Name] LIKE '%connect%' OR [nt].[Name] LIKE '%guest%' OR [nt].[Name] LIKE '%visitor%' OR [nt].[Name] LIKE '%assimil%' OR [nt].[Name] LIKE '%next step%' THEN 'connection'
        WHEN [nt].[Name] LIKE '%staff%' OR [nt].[Name] LIKE '%admin%' OR [nt].[Name] LIKE '%office%' THEN 'staff'
        WHEN [nt].[Name] LIKE '%general%' OR [nt].[Name] LIKE '%personal%' THEN 'general'
        ELSE 'other'
    END AS [Kind],
    CAST( CASE WHEN [nt].[Guid] IN (
        '66A1B9D7-7EFA-40F3-9415-E54437977D60', '5B7BE8F4-481B-4BA3-99A2-B26307A2BA42', 'A3F5982F-C4D0-4345-8021-EB38C4C9AA18',
        'BBADA8EF-23FC-4B46-B7A7-0F6D31F8C045', '4E22E9DA-06A7-45DB-9EB3-BFCB7A2A7F21', 'A480E95A-F941-473E-B200-50D2658C9417',
        '87BACB34-DB87-45E0-AB60-BFABF7CEECDB', 'B54F9D90-9AF3-4E8A-8F33-9338C7C1287F' ) THEN 1 ELSE 0 END AS BIT ) AS [IsRockType],
    [nt].[UserSelectable],
    [nt].[AllowsReplies],
    [nt].[RequiresApprovals],
    [nt].[AllowsWatching],
    [nt].[IsMentionEnabled],
    [nt].[AllowsAttachments],
    CAST( CASE WHEN EXISTS (
        SELECT 1 FROM [Auth] AS [a]
        WHERE [a].[EntityTypeId] = @NoteTypeEntityTypeId AND [a].[EntityId] = [nt].[Id] AND [a].[Action] = 'View'
    ) THEN 1 ELSE 0 END AS BIT ) AS [HasViewSecurity]
INTO #Type
FROM [NoteType] AS [nt]
WHERE [nt].[EntityTypeId] = @PersonEntityTypeId;

-- Staff are active members of Rock's two staff security roles.
SELECT DISTINCT [gm].[PersonId]
INTO #Staff
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [g].[Guid] IN ( '2C112948-FF4C-46E7-981A-0257681EADF4', '300BA2C8-49A3-44BA-A82A-82E3FD8C3745' )
    AND [gm].[GroupMemberStatus] = 1
    AND [gm].[IsArchived] = 0;

-- Notes on people written in the window, with the person's age and gender when the note was written.
SELECT
    [n].[Id] AS [NoteId],
    [n].[NoteTypeId],
    [t].[Kind],
    [t].[IsRockType],
    [t].[HasViewSecurity],
    [n].[EntityId] AS [PersonId],
    [n].[CreatedDateTime],
    [n].[ParentNoteId],
    [n].[IsAlert],
    [n].[IsPrivateNote],
    [n].[IsPinned],
    CAST( CASE WHEN [n].[EditedDateTime] IS NOT NULL THEN 1 ELSE 0 END AS BIT ) AS [IsEdited],
    CAST( CASE WHEN [n].[ForeignId] IS NOT NULL OR [n].[ForeignGuid] IS NOT NULL OR ISNULL( [n].[ForeignKey], '' ) <> '' THEN 1 ELSE 0 END AS BIT ) AS [IsImported],
    CAST( CASE WHEN ISNULL( [n].[Caption], '' ) <> '' THEN 1 ELSE 0 END AS BIT ) AS [HasCaption],
    CAST( CASE WHEN [n].[Text] LIKE '%data-identifier%' THEN 1 ELSE 0 END AS BIT ) AS [HasMention],
    CAST( CASE WHEN [n].[Text] LIKE '%<[a-z]%' THEN 1 ELSE 0 END AS BIT ) AS [HasHtml],
    LEN( [n].[Text] ) AS [TextLength],
    [pa].[PersonId] AS [AuthorPersonId],
    [p].[Gender],
    [p].[RecordStatusValueId],
    CASE WHEN [p].[BirthDate] IS NULL THEN NULL ELSE DATEDIFF( DAY, [p].[BirthDate], [n].[CreatedDateTime] ) / 365.25 END AS [Age]
INTO #Note
FROM [Note] AS [n]
INNER JOIN #Type AS [t] ON [t].[NoteTypeId] = [n].[NoteTypeId]
LEFT JOIN [Person] AS [p] ON [p].[Id] = [n].[EntityId]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [n].[CreatedByPersonAliasId]
WHERE [n].[CreatedDateTime] >= @Start24 AND [n].[CreatedDateTime] < @EndDate;

CREATE CLUSTERED INDEX [IX_NoteId] ON #Note ( [NoteId] );

-- Notes per author.
SELECT [n].[AuthorPersonId], COUNT( * ) AS [Notes], CAST( MAX( CASE WHEN [s].[PersonId] IS NULL THEN 0 ELSE 1 END ) AS BIT ) AS [IsStaff]
INTO #Author
FROM #Note AS [n]
LEFT JOIN #Staff AS [s] ON [s].[PersonId] = [n].[AuthorPersonId]
WHERE [n].[AuthorPersonId] IS NOT NULL
GROUP BY [n].[AuthorPersonId];

-- =============================================
-- Summary and months
-- =============================================

DECLARE @JNoteSummary NVARCHAR(MAX) = (
    SELECT
        COUNT( * ) AS [notes],
        COUNT( DISTINCT [n].[PersonId] ) AS [notedPeople],
        ( SELECT COUNT( * ) FROM #Author ) AS [authors],
        ( SELECT COUNT( * ) FROM [Note] AS [an] INNER JOIN #Type AS [at] ON [at].[NoteTypeId] = [an].[NoteTypeId] ) AS [notesAllTime],
        ( SELECT COUNT( DISTINCT [an].[EntityId] ) FROM [Note] AS [an] INNER JOIN #Type AS [at] ON [at].[NoteTypeId] = [an].[NoteTypeId] ) AS [notedPeopleAllTime],
        CAST( AVG( CASE WHEN [n].[IsRockType] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctOnRockTypes],
        CAST( AVG( CASE WHEN [n].[IsImported] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctImported],
        CAST( AVG( CASE WHEN [n].[AuthorPersonId] IS NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithNoAuthor]
    FROM #Note AS [n]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

DECLARE @JNotesByMonth NVARCHAR(MAX) = (
    SELECT
        CONVERT( CHAR(7), [n].[CreatedDateTime], 120 ) AS [month],
        COUNT( * ) AS [notes]
    FROM #Note AS [n]
    GROUP BY CONVERT( CHAR(7), [n].[CreatedDateTime], 120 )
    ORDER BY CONVERT( CHAR(7), [n].[CreatedDateTime], 120 )
    FOR JSON PATH
);

-- =============================================
-- Note details: flags and text
-- =============================================

DECLARE @JNoteDetail NVARCHAR(MAX) = (
    SELECT [s].*, [tx].*, [rp].*
    FROM (
        SELECT
            CAST( AVG( CASE WHEN [n].[IsAlert] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAlert],
            CAST( AVG( CASE WHEN [n].[IsPrivateNote] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPrivate],
            CAST( AVG( CASE WHEN [n].[IsPinned] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctPinned],
            CAST( AVG( CASE WHEN [n].[ParentNoteId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctReplies],
            CAST( AVG( CASE WHEN [n].[IsEdited] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctEdited],
            CAST( AVG( CASE WHEN [n].[HasCaption] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithCaption],
            CAST( AVG( CASE WHEN [n].[HasMention] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithMention],
            CAST( AVG( CASE WHEN [n].[HasHtml] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWithHtml],
            CAST( AVG( CASE WHEN [n].[HasViewSecurity] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctOnSecuredTypes],
            CAST( AVG( CASE WHEN [n].[AuthorPersonId] = [n].[PersonId] THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAboutTheAuthor],
            CAST( AVG( CASE WHEN CAST( [n].[CreatedDateTime] AS TIME ) = '00:00' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctWrittenAtMidnight]
        FROM #Note AS [n]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.10 ) WITHIN GROUP ( ORDER BY [n].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [textLengthP10],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [n].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [textLengthP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [n].[TextLength] ) OVER () AS DECIMAL(7,0) ) AS [textLengthP90]
        FROM #Note AS [n]
        WHERE [n].[TextLength] > 0
    ) AS [tx]
    OUTER APPLY (
        -- Share of top-level notes that got at least one reply.
        SELECT
            CAST( AVG( CASE WHEN [r].[ParentNoteId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctTopLevelNotesWithReply]
        FROM #Note AS [n]
        LEFT JOIN (
            SELECT DISTINCT [rn].[ParentNoteId]
            FROM [Note] AS [rn]
            WHERE [rn].[ParentNoteId] IS NOT NULL
        ) AS [r] ON [r].[ParentNoteId] = [n].[NoteId]
        WHERE [n].[ParentNoteId] IS NULL
    ) AS [rp]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Kinds of note types (kinds only; type names are never returned)
-- =============================================

DECLARE @JNotesByKind NVARCHAR(MAX) = (
    SELECT
        [k].[Kind] AS [kind],
        CAST( [k].[Notes] * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Note ), 0 ) AS DECIMAL(5,1) ) AS [pctNotes],
        [k].[TypesUsed] AS [typesUsed],
        [k].[PctAlert] AS [pctAlert],
        [k].[PctPrivate] AS [pctPrivate],
        [k].[PctSecured] AS [pctSecured],
        [k].[PctByStaff] AS [pctByStaff],
        [l].[TextLengthP50] AS [textLengthP50]
    FROM (
        SELECT
            [n].[Kind],
            COUNT( * ) AS [Notes],
            COUNT( DISTINCT [n].[NoteTypeId] ) AS [TypesUsed],
            CAST( AVG( CASE WHEN [n].[IsAlert] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [PctAlert],
            CAST( AVG( CASE WHEN [n].[IsPrivateNote] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [PctPrivate],
            CAST( AVG( CASE WHEN [n].[HasViewSecurity] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [PctSecured],
            CAST( AVG( CASE WHEN [s].[PersonId] IS NOT NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [PctByStaff]
        FROM #Note AS [n]
        LEFT JOIN #Staff AS [s] ON [s].[PersonId] = [n].[AuthorPersonId]
        GROUP BY [n].[Kind]
    ) AS [k]
    LEFT JOIN (
        SELECT DISTINCT
            [n].[Kind],
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [n].[TextLength] ) OVER ( PARTITION BY [n].[Kind] ) AS DECIMAL(7,0) ) AS [TextLengthP50]
        FROM #Note AS [n]
        WHERE [n].[TextLength] > 0
    ) AS [l] ON [l].[Kind] = [k].[Kind]
    ORDER BY [k].[Notes] DESC
    FOR JSON PATH
);

-- Note types on people: how many exist, how many are used, and which features they turn on.
DECLARE @JNoteTypes NVARCHAR(MAX) = (
    SELECT [s].*, [u].*, [c].*
    FROM (
        SELECT
            COUNT( * ) AS [typesDefined],
            SUM( CASE WHEN [t].[IsRockType] = 0 THEN 1 ELSE 0 END ) AS [customTypesDefined],
            SUM( CASE WHEN [t].[UserSelectable] = 1 THEN 1 ELSE 0 END ) AS [userSelectableTypes]
        FROM #Type AS [t]
    ) AS [s]
    OUTER APPLY (
        SELECT
            COUNT( * ) AS [typesUsed],
            CAST( AVG( CASE WHEN [t].[AllowsReplies] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUsedTypesAllowReplies],
            CAST( AVG( CASE WHEN [t].[RequiresApprovals] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUsedTypesRequireApprovals],
            CAST( AVG( CASE WHEN [t].[AllowsWatching] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUsedTypesAllowWatching],
            CAST( AVG( CASE WHEN [t].[IsMentionEnabled] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUsedTypesMentionEnabled],
            CAST( AVG( CASE WHEN [t].[AllowsAttachments] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUsedTypesAllowAttachments],
            CAST( AVG( CASE WHEN [t].[HasViewSecurity] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctUsedTypesWithViewSecurity]
        FROM #Type AS [t]
        WHERE EXISTS ( SELECT 1 FROM #Note AS [n] WHERE [n].[NoteTypeId] = [t].[NoteTypeId] )
    ) AS [u]
    OUTER APPLY (
        SELECT
            CAST( SUM( CASE WHEN [x].[Rank] = 1 THEN [x].[Notes] ELSE 0 END ) * 100.0 / NULLIF( SUM( [x].[Notes] ), 0 ) AS DECIMAL(5,1) ) AS [pctInTopType],
            CAST( SUM( CASE WHEN [x].[Rank] <= 3 THEN [x].[Notes] ELSE 0 END ) * 100.0 / NULLIF( SUM( [x].[Notes] ), 0 ) AS DECIMAL(5,1) ) AS [pctInTop3Types]
        FROM (
            SELECT COUNT( * ) AS [Notes], ROW_NUMBER() OVER ( ORDER BY COUNT( * ) DESC ) AS [Rank]
            FROM #Note AS [n]
            GROUP BY [n].[NoteTypeId]
        ) AS [x]
    ) AS [c]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- When notes are written (day of week and time of day)
-- =============================================

-- 1900-01-01 was a Monday, so this weekday math does not depend on DATEFIRST or the server language.
DECLARE @JNotesByWeekday NVARCHAR(MAX) = (
    SELECT
        CHOOSE( [w].[DayNumber] + 1, 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun' ) AS [weekday],
        CAST( COUNT( * ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Note ), 0 ) AS DECIMAL(5,1) ) AS [pctNotes]
    FROM (
        SELECT DATEDIFF( DAY, '19000101', CAST( [n].[CreatedDateTime] AS DATE ) ) % 7 AS [DayNumber]
        FROM #Note AS [n]
    ) AS [w]
    GROUP BY [w].[DayNumber]
    ORDER BY [w].[DayNumber]
    FOR JSON PATH
);

DECLARE @JNotesByHourBand NVARCHAR(MAX) = (
    SELECT
        [h].[HourBand] AS [hourBand],
        CAST( COUNT( * ) * 100.0 / NULLIF( ( SELECT COUNT( * ) FROM #Note ), 0 ) AS DECIMAL(5,1) ) AS [pctNotes]
    FROM (
        SELECT
            CASE
                WHEN DATEPART( HOUR, [n].[CreatedDateTime] ) < 6 THEN '00-05'
                WHEN DATEPART( HOUR, [n].[CreatedDateTime] ) < 9 THEN '06-08'
                WHEN DATEPART( HOUR, [n].[CreatedDateTime] ) < 12 THEN '09-11'
                WHEN DATEPART( HOUR, [n].[CreatedDateTime] ) < 15 THEN '12-14'
                WHEN DATEPART( HOUR, [n].[CreatedDateTime] ) < 18 THEN '15-17'
                ELSE '18-23'
            END AS [HourBand]
        FROM #Note AS [n]
    ) AS [h]
    GROUP BY [h].[HourBand]
    ORDER BY [h].[HourBand]
    FOR JSON PATH
);

-- =============================================
-- Who writes notes
-- =============================================

DECLARE @JNoteAuthors NVARCHAR(MAX) = (
    SELECT [s].*, [pp].*, [cn].*
    FROM (
        SELECT
            COUNT( * ) AS [authors],
            SUM( CASE WHEN [a].[IsStaff] = 1 THEN 1 ELSE 0 END ) AS [staffAuthors],
            ( SELECT COUNT( * ) FROM #Staff ) AS [staff],
            CAST( SUM( CASE WHEN [a].[IsStaff] = 1 THEN [a].[Notes] ELSE 0 END ) * 100.0 / NULLIF( SUM( [a].[Notes] ), 0 ) AS DECIMAL(5,1) ) AS [pctNotesByStaff]
        FROM #Author AS [a]
    ) AS [s]
    OUTER APPLY (
        SELECT TOP 1
            CAST( PERCENTILE_CONT( 0.50 ) WITHIN GROUP ( ORDER BY [a].[Notes] ) OVER () AS DECIMAL(7,1) ) AS [notesPerAuthorP50],
            CAST( PERCENTILE_CONT( 0.90 ) WITHIN GROUP ( ORDER BY [a].[Notes] ) OVER () AS DECIMAL(7,1) ) AS [notesPerAuthorP90]
        FROM #Author AS [a]
    ) AS [pp]
    OUTER APPLY (
        -- Share of all notes written by the busiest tenth of authors.
        SELECT
            CAST( SUM( CASE WHEN [x].[Tenth] = 1 THEN [x].[Notes] ELSE 0 END ) * 100.0 / NULLIF( SUM( [x].[Notes] ), 0 ) AS DECIMAL(5,1) ) AS [pctNotesByTop10PctOfAuthors]
        FROM (
            SELECT [a].[Notes], NTILE( 10 ) OVER ( ORDER BY [a].[Notes] DESC ) AS [Tenth]
            FROM #Author AS [a]
        ) AS [x]
    ) AS [cn]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Who notes are about
-- =============================================

DECLARE @JNotedPeople NVARCHAR(MAX) = (
    SELECT [s].*, [q].*
    FROM (
        SELECT
            CAST( AVG( CASE WHEN [n].[Age] < 13 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAgeUnder13],
            CAST( AVG( CASE WHEN [n].[Age] >= 13 AND [n].[Age] < 18 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge13To17],
            CAST( AVG( CASE WHEN [n].[Age] >= 18 AND [n].[Age] < 30 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge18To29],
            CAST( AVG( CASE WHEN [n].[Age] >= 30 AND [n].[Age] < 45 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge30To44],
            CAST( AVG( CASE WHEN [n].[Age] >= 45 AND [n].[Age] < 65 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge45To64],
            CAST( AVG( CASE WHEN [n].[Age] >= 65 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAge65Plus],
            CAST( AVG( CASE WHEN [n].[Age] IS NULL THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAgeUnknown],
            CAST( AVG( CASE WHEN [n].[Gender] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctMale],
            CAST( AVG( CASE WHEN [n].[Gender] = 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctFemale],
            CAST( AVG( CASE WHEN ISNULL( [n].[Gender], 0 ) = 0 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctGenderUnknown],
            CAST( AVG( CASE WHEN [dv].[Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctAboutActiveRecords]
        FROM #Note AS [n]
        LEFT JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [n].[RecordStatusValueId]
    ) AS [s]
    OUTER APPLY (
        SELECT
            CAST( AVG( CASE WHEN [x].[Notes] = 1 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNotedPeopleWith1Note],
            CAST( AVG( CASE WHEN [x].[Notes] BETWEEN 2 AND 3 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNotedPeopleWith2To3Notes],
            CAST( AVG( CASE WHEN [x].[Notes] BETWEEN 4 AND 9 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNotedPeopleWith4To9Notes],
            CAST( AVG( CASE WHEN [x].[Notes] >= 10 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNotedPeopleWith10PlusNotes],
            CAST( AVG( CASE WHEN [x].[Kinds] >= 2 THEN 100.0 ELSE 0.0 END ) AS DECIMAL(5,1) ) AS [pctNotedPeopleWith2PlusKinds]
        FROM (
            SELECT [n].[PersonId], COUNT( * ) AS [Notes], COUNT( DISTINCT [n].[Kind] ) AS [Kinds]
            FROM #Note AS [n]
            GROUP BY [n].[PersonId]
        ) AS [x]
    ) AS [q]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

-- =============================================
-- Output: one row per section. Rock's SQL editor shows only this result.
-- =============================================

DECLARE @JMeta NVARCHAR(MAX) = (
    SELECT
        @ChurchLabel AS [church],
        6 AS [part],
        CONVERT( CHAR(10), @Start24, 120 ) AS [periodStart],
        CONVERT( CHAR(10), @EndDate, 120 ) AS [periodEndExclusive]
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT [s].[Section], [s].[Json]
FROM ( VALUES
    ( 1, 'meta', @JMeta ),
    ( 2, 'noteSummary', @JNoteSummary ),
    ( 3, 'notesByMonth', @JNotesByMonth ),
    ( 4, 'noteDetail', @JNoteDetail ),
    ( 5, 'notesByKind', @JNotesByKind ),
    ( 6, 'noteTypes', @JNoteTypes ),
    ( 7, 'notesByWeekday', @JNotesByWeekday ),
    ( 8, 'notesByHourBand', @JNotesByHourBand ),
    ( 9, 'noteAuthors', @JNoteAuthors ),
    ( 10, 'notedPeople', @JNotedPeople )
) AS [s] ( [Order], [Section], [Json] )
ORDER BY [s].[Order];

DROP TABLE IF EXISTS #Type, #Staff, #Note, #Author;
