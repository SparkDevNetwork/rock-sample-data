/*
    Invariants: rules that must hold in every loaded dataset, on every Rock version.

    The loader bulk-copies around Rock's save hooks, so Rock never gets to enforce these itself. The verify tool
    (verify/README.md) runs each check after a load and fails when any returns more than zero. Each check starts
    with "-- check: name", says why it matters on the next "-- why:" line, and returns one number: how many rows
    break the rule. @ForeignKey is the dataset's key, for example SampleData:core-1. @CardPeople and @CardHouseholds
    are the dataset card's numbers from datasets/{key}.json, or null when the load used another seed than the
    default, which the card was not measured at. @AdminPersonId is Rock's admin person (Person Id 1), which the load
    turns into a Cast person but leaves without the dataset's ForeignKey (decision 177), so rules about this
    dataset's people name it alongside @ForeignKey.

    Rules that a foreign key or unique index already enforces are left out: the bulk copy checks those.
    "Every family has an adult" is left out on purpose: kids whose parents are not in Rock, and Jenny Greggs's
    memorial record, are child-only families, as they are in real churches.
*/

-- check: dataset-has-people
-- why: Every rule below looks only at this dataset's rows. A load that wrote nothing would pass them all.
SELECT CASE WHEN EXISTS ( SELECT 1 FROM [Person] WHERE [ForeignKey] = @ForeignKey ) THEN 0 ELSE 1 END;

-- check: admin-is-a-cast-person
-- why: Decision 177. Rock's admin person becomes the Cast person who replaces it, so signing in as admin shows real data. It keeps Rock's admin login, Rock Administrators, and an empty ForeignKey, so nothing that removes sample data by ForeignKey can remove the site's admin. Its primary family is this dataset's, and it is in no other family, so Rock's empty Admin family is gone.
SELECT CASE WHEN EXISTS (
        SELECT 1
        FROM [Person] AS [p]
        INNER JOIN [Group] AS [f] ON [f].[Id] = [p].[PrimaryFamilyId]
        WHERE [p].[Id] = @AdminPersonId AND [p].[ForeignKey] IS NULL AND [f].[ForeignKey] = @ForeignKey
            AND EXISTS ( SELECT 1 FROM [UserLogin] AS [ul] WHERE [ul].[PersonId] = [p].[Id] AND [ul].[UserName] = 'admin' )
            AND EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] WHERE [gm].[PersonId] = [p].[Id] AND [g].[Guid] = '628C51A8-4613-43ED-A18D-4A6FB999273E' ) )
    AND NOT EXISTS (
        SELECT 1
        FROM [GroupMember] AS [gm]
        INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
        INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
        WHERE [gm].[PersonId] = @AdminPersonId AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' AND ISNULL( [g].[ForeignKey], '' ) <> @ForeignKey )
    THEN 0 ELSE 1 END;

-- check: person-has-one-primary-alias
-- why: Rock finds a person through their primary alias; every audit column and gift points at it.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ( [p].[PrimaryAliasId] IS NULL
        OR NOT EXISTS ( SELECT 1 FROM [PersonAlias] AS [pa] WHERE [pa].[Id] = [p].[PrimaryAliasId] AND [pa].[PersonId] = [p].[Id] AND [pa].[AliasPersonId] = [p].[Id] )
        OR ( SELECT COUNT(*) FROM [PersonAlias] AS [pa] WHERE [pa].[PersonId] = [p].[Id] AND [pa].[AliasPersonId] = [p].[Id] ) <> 1 );

-- check: person-is-in-a-family
-- why: Rock expects every person to belong to at least one family; profile pages and check-in break without one.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND NOT EXISTS (
        SELECT 1
        FROM [GroupMember] AS [gm]
        INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
        INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
        WHERE [gm].[PersonId] = [p].[Id] AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' );

-- check: primary-family-is-a-family-the-person-is-in
-- why: Rock's save hook sets the primary family and campus; the loader fills them in itself.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ( [p].[PrimaryFamilyId] IS NULL
        OR NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [p].[PrimaryFamilyId] AND [gm].[PersonId] = [p].[Id] ) );

-- check: primary-campus-is-the-primary-family-campus
-- why: Rock shows and filters people by the campus of their primary family.
SELECT COUNT(*)
FROM [Person] AS [p]
INNER JOIN [Group] AS [f] ON [f].[Id] = [p].[PrimaryFamilyId]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ISNULL( [p].[PrimaryCampusId], -1 ) <> ISNULL( [f].[CampusId], -1 );

-- check: giving-id-matches-giving-group
-- why: Rock groups gifts by GivingId (G + giving group, or P + person). A wrong one splits or merges giving units.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ( ISNULL( [p].[GivingId], '' ) <> CASE WHEN [p].[GivingGroupId] IS NULL THEN 'P' + CAST( [p].[Id] AS VARCHAR(20) ) ELSE 'G' + CAST( [p].[GivingGroupId] AS VARCHAR(20) ) END
        OR [p].[GivingLeaderId] IS NULL );

-- check: giving-leader-is-in-the-giving-unit
-- why: The giving leader receives the statement; it must be the person or an adult of their giving group.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND [p].[GivingLeaderId] <> [p].[Id]
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [p].[GivingGroupId] AND [gm].[PersonId] = [p].[GivingLeaderId] );

-- check: birth-date-parts-agree
-- why: Rock stores the birth date three ways (date, day/month/year, and a sortable key); reports read all three.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND [p].[BirthDate] IS NOT NULL
    AND ( [p].[BirthYear] <> YEAR( [p].[BirthDate] ) OR [p].[BirthMonth] <> MONTH( [p].[BirthDate] ) OR [p].[BirthDay] <> DAY( [p].[BirthDate] )
        OR [p].[BirthDateKey] <> YEAR( [p].[BirthDate] ) * 10000 + MONTH( [p].[BirthDate] ) * 100 + DAY( [p].[BirthDate] ) );

-- check: phones-use-the-reserved-range
-- why: Content standard 2. Phones are 555-0100 to 555-0199 in a Phoenix-area code, so no real person gets a call.
SELECT COUNT(*)
FROM [PhoneNumber] AS [pn]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pn].[PersonId]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ( LEN( [pn].[Number] ) <> 10 OR LEFT( [pn].[Number], 3 ) NOT IN ( '602', '480', '623' ) OR SUBSTRING( [pn].[Number], 4, 5 ) <> '55501' );

-- check: emails-use-reserved-domains
-- why: Content standard 3. Emails use example.org, or rocksolidchurchdemo.com for the sample church's staff.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ISNULL( [p].[Email], '' ) <> ''
    AND [p].[Email] NOT LIKE '%@example.org'
    AND [p].[Email] NOT LIKE '%@rocksolidchurchdemo.com';

-- check: no-race-or-ethnicity
-- why: Content standard 8. Photo ethnicity only makes matching family photos; it never goes into Rock.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND ( [p].[RaceValueId] IS NOT NULL OR [p].[EthnicityValueId] IS NOT NULL );

-- check: home-addresses-are-mapped
-- why: Group Finder, campus distance, and maps need a point for every home address.
SELECT COUNT(*)
FROM [GroupLocation] AS [gl]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gl].[GroupId]
INNER JOIN [Location] AS [l] ON [l].[Id] = [gl].[LocationId]
INNER JOIN [DefinedValue] AS [t] ON [t].[Id] = [gl].[GroupLocationTypeValueId]
WHERE [g].[ForeignKey] = @ForeignKey
    AND [t].[Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC'
    AND [l].[GeoPoint] IS NULL;

-- check: nothing-dated-in-the-future
-- why: The timeline counts back from the install day. A future gift, check-in, or step shows up in "upcoming" places.
--      Requests to serve in the weeks ahead are the one exception: they are scheduled and not attended (decision 74).
SELECT ( SELECT COUNT(*) FROM [Attendance] WHERE [ForeignKey] = @ForeignKey AND [StartDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) AND NOT ( [RequestedToAttend] = 1 AND [DidAttend] = 0 ) )
    + ( SELECT COUNT(*) FROM [FinancialTransaction] WHERE [ForeignKey] = @ForeignKey AND [TransactionDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) )
    + ( SELECT COUNT(*) FROM [Step] WHERE [ForeignKey] = @ForeignKey
            AND ( [StartDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) OR [EndDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) OR [CompletedDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) ) )
    + ( SELECT COUNT(*) FROM [StepProgramCompletion] WHERE [ForeignKey] = @ForeignKey AND [EndDateTime] >= DATEADD( DAY, 1, CAST( GETDATE() AS DATE ) ) );

-- check: transaction-has-positive-details
-- why: Rock totals a gift from its details. A gift with no detail, or a zero or negative one, breaks statements.
SELECT COUNT(*)
FROM [FinancialTransaction] AS [t]
WHERE [t].[ForeignKey] = @ForeignKey
    AND ( NOT EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] WHERE [d].[TransactionId] = [t].[Id] )
        OR EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] WHERE [d].[TransactionId] = [t].[Id] AND [d].[Amount] <= 0 ) );

-- check: transaction-is-inside-its-batch
-- why: Batches cover a date range. A gift outside its batch's range lands in the wrong batch report.
SELECT COUNT(*)
FROM [FinancialTransaction] AS [t]
INNER JOIN [FinancialBatch] AS [b] ON [b].[Id] = [t].[BatchId]
WHERE [t].[ForeignKey] = @ForeignKey
    AND ( [t].[TransactionDateTime] < [b].[BatchStartDateTime] OR [t].[TransactionDateTime] >= [b].[BatchEndDateTime] );

-- check: member-role-belongs-to-the-group-type
-- why: Rock only lets a member hold a role of their group's type. The database does not enforce it.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
WHERE [gm].[ForeignKey] = @ForeignKey
    AND [r].[GroupTypeId] <> [g].[GroupTypeId];

-- check: one-current-history-row
-- why: Rock's history tables mark exactly one current row per group, member, and meeting location.
SELECT ( SELECT COUNT(*) FROM ( SELECT [GroupId] FROM [GroupHistorical] WHERE [ForeignKey] = @ForeignKey GROUP BY [GroupId] HAVING SUM( CASE WHEN [CurrentRowIndicator] = 1 THEN 1 ELSE 0 END ) <> 1 ) AS [x] )
    + ( SELECT COUNT(*) FROM ( SELECT [GroupMemberId] FROM [GroupMemberHistorical] WHERE [ForeignKey] = @ForeignKey GROUP BY [GroupMemberId] HAVING SUM( CASE WHEN [CurrentRowIndicator] = 1 THEN 1 ELSE 0 END ) <> 1 ) AS [x] )
    + ( SELECT COUNT(*) FROM ( SELECT [GroupLocationId] FROM [GroupLocationHistorical] WHERE [ForeignKey] = @ForeignKey GROUP BY [GroupLocationId] HAVING SUM( CASE WHEN [CurrentRowIndicator] = 1 THEN 1 ELSE 0 END ) <> 1 ) AS [x] );

-- check: photo-is-a-stored-person-image
-- why: A photo id must point at a Person Image file whose bytes are in the database, or the profile shows a broken image.
SELECT COUNT(*)
FROM [Person] AS [p]
LEFT JOIN [BinaryFile] AS [f] ON [f].[Id] = [p].[PhotoId]
LEFT JOIN [BinaryFileType] AS [ft] ON [ft].[Id] = [f].[BinaryFileTypeId]
WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
    AND [p].[PhotoId] IS NOT NULL
    AND ( [f].[Id] IS NULL
        OR [ft].[Guid] <> '03BD8476-8A9F-4078-B628-5B538F967AFC'
        OR NOT EXISTS ( SELECT 1 FROM [BinaryFileData] AS [d] WHERE [d].[Id] = [f].[Id] AND DATALENGTH( [d].[Content] ) > 0 ) );

-- check: only-this-dataset
-- why: A database holds one dataset. Rows from another would mean a load on a database that was not fresh.
SELECT ( SELECT COUNT(*) FROM [Person] WHERE [ForeignKey] LIKE 'SampleData:%' AND [ForeignKey] <> @ForeignKey )
    + ( SELECT COUNT(*) FROM [Group] WHERE [ForeignKey] LIKE 'SampleData:%' AND [ForeignKey] <> @ForeignKey )
    + ( SELECT COUNT(*) FROM [FinancialTransaction] WHERE [ForeignKey] LIKE 'SampleData:%' AND [ForeignKey] <> @ForeignKey );

-- check: note-is-on-a-person-of-this-dataset
-- why: A person note's EntityId must point at a loaded person, or the note is an orphan Rock never shows.
SELECT COUNT(*)
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nt].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
WHERE [n].[ForeignKey] = @ForeignKey
    AND NOT EXISTS ( SELECT 1 FROM [Person] AS [p] WHERE [p].[Id] = [n].[EntityId] AND ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) );

-- check: note-reply-matches-its-parent
-- why: Rock shows a reply under its parent, so it must be on the same entity, of the same type, and written after it.
SELECT COUNT(*)
FROM [Note] AS [r]
INNER JOIN [Note] AS [n] ON [n].[Id] = [r].[ParentNoteId]
WHERE [r].[ForeignKey] = @ForeignKey
    AND ( [r].[EntityId] <> [n].[EntityId] OR [r].[NoteTypeId] <> [n].[NoteTypeId] OR [r].[CreatedDateTime] < [n].[CreatedDateTime] );

-- check: note-author-is-not-the-noted-person
-- why: Staff write notes about other people; a note about its own author looks like a load error.
SELECT COUNT(*)
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nt].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [n].[CreatedByPersonAliasId]
WHERE [n].[ForeignKey] = @ForeignKey
    AND [pa].[PersonId] = [n].[EntityId];

-- check: person-note-looks-saved-by-rock
-- why: Rock's save hook approves notes whose type needs no approval, and its editor sets EditedDateTime and a private note's caption. Unsent flags would make Rock's Send Note Notifications job email staff about old notes.
SELECT COUNT(*)
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [nt].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
WHERE [n].[ForeignKey] = @ForeignKey
    AND ( [n].[ApprovalStatus] <> 1
        OR [n].[NotificationsSent] = 0
        OR [n].[ApprovalsSent] = 0
        OR [n].[EditedDateTime] IS NULL
        OR [n].[CreatedByPersonAliasId] IS NULL
        OR ( [n].[IsPrivateNote] = 1 AND ISNULL( [n].[Caption], '' ) <> 'You - Personal Note' ) );

-- check: no-high-schooler-in-adult-groups
-- why: Small groups and serving teams are adult-only. Nobody joins or leads one while still in high school: a membership starts on or after the June 1 grade change in the person's graduation year (decision 169).
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gm].[ForeignKey] = @ForeignKey
    AND [gt].[Guid] IN ( '50FCFB30-F51A-49DF-86F4-2B176EA1820B', '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4' )
    AND [p].[GraduationYear] IS NOT NULL
    AND [gm].[DateTimeAdded] < DATEFROMPARTS( [p].[GraduationYear], 6, 1 );

-- check: step-matches-its-membership
-- why: A Small Group, Serve, or Lead step records one membership (decision 86): same person, group, and dates, In Progress only while the member is Pending. And every membership that counts has its step.
WITH [Memberships] AS (
    SELECT [p].[PrimaryAliasId], [gm].[GroupId], [gt].[Guid] AS [GroupTypeGuid], [r].[IsLeader], [gm].[GroupMemberStatus], [gm].[DateTimeAdded],
        CASE WHEN [gm].[GroupMemberStatus] = 0 OR [gm].[IsArchived] = 1 THEN ISNULL( [gm].[InactiveDateTime], [gm].[ArchivedDateTime] ) END AS [MemberEnded],
        CASE WHEN [g].[IsActive] = 0 OR [g].[IsArchived] = 1 THEN ISNULL( [g].[InactiveDateTime], [g].[ArchivedDateTime] ) END AS [GroupEnded]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
    WHERE [gm].[ForeignKey] = @ForeignKey
        AND [gt].[Guid] IN ( '50FCFB30-F51A-49DF-86F4-2B176EA1820B', '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4' )
),
[Expected] AS (
    SELECT [PrimaryAliasId], [GroupId], CAST( 'EFA15A4F-5666-4153-B92F-AF3ECD73C504' AS UNIQUEIDENTIFIER ) AS [StepTypeGuid], [DateTimeAdded], [MemberEnded], [GroupEnded], 0 AS [IsInProgress]
    FROM [Memberships] WHERE [GroupTypeGuid] = '50FCFB30-F51A-49DF-86F4-2B176EA1820B' AND [GroupMemberStatus] <> 2
    UNION ALL
    SELECT [PrimaryAliasId], [GroupId], CAST( '71E66730-8F7D-4EEF-9C53-524C4BDE5E59' AS UNIQUEIDENTIFIER ), [DateTimeAdded], [MemberEnded], [GroupEnded], CASE WHEN [GroupMemberStatus] = 2 THEN 1 ELSE 0 END
    FROM [Memberships] WHERE [GroupTypeGuid] = '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4'
    UNION ALL
    SELECT [PrimaryAliasId], [GroupId], CAST( 'CFF0E432-3AB6-4290-8F6E-C1A3D8D8F375' AS UNIQUEIDENTIFIER ), [DateTimeAdded], [MemberEnded], [GroupEnded], 0
    FROM [Memberships] WHERE [IsLeader] = 1 AND [GroupMemberStatus] <> 2
),
[ExpectedSteps] AS (
    SELECT [PrimaryAliasId], [GroupId], [StepTypeGuid], [DateTimeAdded], [IsInProgress],
        CASE WHEN [MemberEnded] IS NULL THEN [GroupEnded] WHEN [GroupEnded] IS NULL OR [MemberEnded] < [GroupEnded] THEN [MemberEnded] ELSE [GroupEnded] END AS [Ended]
    FROM [Expected]
),
[Steps] AS (
    SELECT [s].[PersonAliasId], [s].[RelatedEntityId], [st].[Guid] AS [StepTypeGuid], [s].[StartDateTime], [s].[EndDateTime], CASE WHEN [ss].[IsCompleteStatus] = 1 THEN 0 ELSE 1 END AS [IsInProgress]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
    WHERE [s].[ForeignKey] = @ForeignKey
        AND [st].[Guid] IN ( 'EFA15A4F-5666-4153-B92F-AF3ECD73C504', '71E66730-8F7D-4EEF-9C53-524C4BDE5E59', 'CFF0E432-3AB6-4290-8F6E-C1A3D8D8F375' )
)
SELECT ( SELECT COUNT(*) FROM [Steps] AS [s] WHERE NOT EXISTS (
            SELECT 1 FROM [ExpectedSteps] AS [e]
            WHERE [e].[PrimaryAliasId] = [s].[PersonAliasId] AND [e].[GroupId] = [s].[RelatedEntityId] AND [e].[StepTypeGuid] = [s].[StepTypeGuid]
                AND [e].[DateTimeAdded] = [s].[StartDateTime] AND ISNULL( [e].[Ended], '9999-01-01' ) = ISNULL( [s].[EndDateTime], '9999-01-01' ) AND [e].[IsInProgress] = [s].[IsInProgress] ) )
    + ( SELECT COUNT(*) FROM [ExpectedSteps] AS [e] WHERE NOT EXISTS (
            SELECT 1 FROM [Steps] AS [s]
            WHERE [e].[PrimaryAliasId] = [s].[PersonAliasId] AND [e].[GroupId] = [s].[RelatedEntityId] AND [e].[StepTypeGuid] = [s].[StepTypeGuid] AND [e].[DateTimeAdded] = [s].[StartDateTime] ) );

-- check: step-looks-saved-by-rock
-- why: Rock's step model fills the date keys from the dates, a Complete step has a completion date and an In Progress one has none, and the sample data puts every step on the person's primary alias and campus.
SELECT COUNT(*)
FROM [Step] AS [s]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [s].[ForeignKey] = @ForeignKey
    AND ( ISNULL( [s].[StartDateKey], 0 ) <> ISNULL( CONVERT( INT, FORMAT( [s].[StartDateTime], 'yyyyMMdd' ) ), 0 )
        OR ISNULL( [s].[EndDateKey], 0 ) <> ISNULL( CONVERT( INT, FORMAT( [s].[EndDateTime], 'yyyyMMdd' ) ), 0 )
        OR ISNULL( [s].[CompletedDateKey], 0 ) <> ISNULL( CONVERT( INT, FORMAT( [s].[CompletedDateTime], 'yyyyMMdd' ) ), 0 )
        OR ( [ss].[IsCompleteStatus] = 1 AND [s].[CompletedDateTime] IS NULL )
        OR ( [ss].[IsCompleteStatus] = 0 AND [s].[CompletedDateTime] IS NOT NULL )
        OR [s].[StartDateTime] IS NULL
        OR ISNULL( [s].[CampusId], 0 ) <> ISNULL( [p].[PrimaryCampusId], 0 )
        OR [s].[PersonAliasId] <> [p].[PrimaryAliasId] );

-- check: step-has-its-person-history
-- why: Rock's step save hook writes person history: one "Added" row per step on the day it was made, and a status change on the day a step that started In Progress was completed. The person's History tab shows each step.
WITH [StepHistory] AS (
    SELECT [h].*
    FROM [History] AS [h]
    INNER JOIN [Category] AS [c] ON [c].[Id] = [h].[CategoryId]
    WHERE [h].[ForeignKey] = @ForeignKey AND [c].[Guid] = '517BB2D3-0A50-4132-818E-63BB3C81EAE9'
)
SELECT ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
        WHERE [s].[ForeignKey] = @ForeignKey
            AND ( SELECT COUNT(*) FROM [StepHistory] AS [h]
                WHERE [h].[RelatedEntityId] = [s].[Id] AND [h].[EntityId] = [pa].[PersonId] AND [h].[Verb] = 'STEPADDED'
                    AND [h].[ValueName] = [st].[Name] AND [h].[CreatedDateTime] = [s].[StartDateTime] ) <> 1 )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
        WHERE [s].[ForeignKey] = @ForeignKey AND [s].[CompletedDateTime] > [s].[StartDateTime]
            AND NOT EXISTS ( SELECT 1 FROM [StepHistory] AS [h]
                WHERE [h].[RelatedEntityId] = [s].[Id] AND [h].[EntityId] = [pa].[PersonId] AND [h].[Verb] = 'MODIFY'
                    AND [h].[ValueName] = 'Step Status' AND [h].[CreatedDateTime] = [s].[CompletedDateTime] ) )
    + ( SELECT COUNT(*)
        FROM [StepHistory] AS [h]
        WHERE NOT EXISTS ( SELECT 1 FROM [Step] AS [s]
            INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
            WHERE [s].[Id] = [h].[RelatedEntityId] AND [pa].[PersonId] = [h].[EntityId] ) );

-- check: baptism-step-agrees-with-baptism-date
-- why: The Baptism step and Rock's Baptism Date person attribute always agree, and the step's Baptized Here matches the person's.
WITH [Dates] AS (
    SELECT [av].[EntityId] AS [PersonId], [av].[ValueAsDateTime]
    FROM [AttributeValue] AS [av]
    INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
    WHERE [a].[Guid] = 'D42763FA-28E9-4A55-A25A-48998D7D7FEF' AND ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) AND ISNULL( [av].[Value], '' ) <> ''
),
[Baptisms] AS (
    SELECT [pa].[PersonId], [s].[Id], [s].[StartDateTime], [s].[CompletedDateTime]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
    WHERE [st].[Guid] = '801CC43C-0641-4271-939E-75E428F31D06' AND [s].[ForeignKey] = @ForeignKey
),
[Here] AS (
    SELECT [b].[Id],
        ( SELECT [av].[Value] FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId] WHERE [a].[Guid] = '343A557E-92CC-4A75-B251-0DA732F5EB4E' AND [av].[EntityId] = [b].[Id] ) AS [OnStep],
        ( SELECT [av].[Value] FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId] WHERE [a].[Guid] = '87986F92-4AC3-49D2-9753-328588F3BB7A' AND [av].[EntityId] = [b].[PersonId] ) AS [OnPerson]
    FROM [Baptisms] AS [b]
)
SELECT ( SELECT COUNT(*) FROM [Dates] AS [d] WHERE NOT EXISTS ( SELECT 1 FROM [Baptisms] AS [b] WHERE [b].[PersonId] = [d].[PersonId] AND [b].[StartDateTime] = [d].[ValueAsDateTime] AND [b].[CompletedDateTime] = [d].[ValueAsDateTime] ) )
    + ( SELECT COUNT(*) FROM [Baptisms] AS [b] WHERE NOT EXISTS ( SELECT 1 FROM [Dates] AS [d] WHERE [d].[PersonId] = [b].[PersonId] AND [d].[ValueAsDateTime] = [b].[StartDateTime] ) )
    + ( SELECT COUNT(*) FROM [Here] WHERE [OnStep] IS NULL OR [OnPerson] IS NULL OR [OnStep] <> [OnPerson] );

-- check: serve-step-follows-starting-point
-- why: Rock's one Discipleship Path prerequisite: Serve needs a completed Starting Point Class first (decision 84). Every volunteer has one before their Serve step starts.
SELECT COUNT(*)
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
WHERE [st].[Guid] = '71E66730-8F7D-4EEF-9C53-524C4BDE5E59' AND [s].[ForeignKey] = @ForeignKey
    AND NOT EXISTS (
        SELECT 1
        FROM [Step] AS [c]
        INNER JOIN [StepType] AS [ct] ON [ct].[Id] = [c].[StepTypeId]
        INNER JOIN [StepStatus] AS [cs] ON [cs].[Id] = [c].[StepStatusId]
        WHERE [ct].[Guid] = 'F3585131-3EBF-49C4-83A0-3F88271A9B9B' AND [c].[PersonAliasId] = [s].[PersonAliasId]
            AND [cs].[IsCompleteStatus] = 1 AND [c].[CompletedDateTime] < [s].[StartDateTime] );

-- check: give-step-agrees-with-journey-stage
-- why: A Give step runs from the week a giver becomes Consistent to the week they become Lapsed or Former (decision 85). So a giver Rock shows as Consistent has an open Give step, and an open Give step belongs to a Consistent or Occasional giver.
WITH [Stages] AS (
    SELECT [p].[PrimaryAliasId], [av].[Value] AS [Stage]
    FROM [Person] AS [p]
    INNER JOIN [AttributeValue] AS [av] ON [av].[EntityId] = [p].[Id]
    INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
    WHERE [a].[Guid] = '13C55AEA-6D88-4470-B3AE-EE5138F044DF' AND ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId )
        AND [p].[RecordTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = 'BF64ADD3-E70A-44CE-9C4B-E76BBED37550' )
),
[OpenGive] AS (
    SELECT DISTINCT [s].[PersonAliasId]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    WHERE [st].[Guid] = 'CE640CD3-DD6E-4BF3-ADDD-953F086E6E14' AND [s].[ForeignKey] = @ForeignKey AND [s].[EndDateTime] IS NULL
)
SELECT ( SELECT COUNT(*) FROM [Stages] AS [x] WHERE [x].[Stage] = '2' AND NOT EXISTS ( SELECT 1 FROM [OpenGive] AS [o] WHERE [o].[PersonAliasId] = [x].[PrimaryAliasId] ) )
    + ( SELECT COUNT(*) FROM [OpenGive] AS [o] WHERE NOT EXISTS ( SELECT 1 FROM [Stages] AS [x] WHERE [x].[PrimaryAliasId] = [o].[PersonAliasId] AND [x].[Stage] IN ( '2', '3' ) ) );

-- check: program-completions-follow-rocks-rule
-- why: Rock's Update Step Program Completions job records a completion for each person whose Complete steps cover every active step type in a program, dated from those steps, and points every step of theirs in the program at it. It runs for every program the dataset has steps in. Rock's job must find nothing to add or fix after install.
WITH [Programs] AS (
    SELECT DISTINCT [st].[StepProgramId] AS [Id]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    WHERE [s].[ForeignKey] = @ForeignKey
),
[PeopleWithAllSteps] AS (
    SELECT [st].[StepProgramId], [s].[PersonAliasId],
        MIN( COALESCE( [s].[StartDateTime], [s].[CreatedDateTime] ) ) AS [StartDateTime],
        MAX( COALESCE( [s].[CompletedDateTime], [s].[EndDateTime] ) ) AS [EndDateTime],
        MAX( [s].[CampusId] ) AS [CampusId]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId] AND [ss].[IsCompleteStatus] = 1
    WHERE [st].[StepProgramId] IN ( SELECT [Id] FROM [Programs] ) AND [s].[ForeignKey] = @ForeignKey
    GROUP BY [st].[StepProgramId], [s].[PersonAliasId]
    HAVING COUNT( DISTINCT [s].[StepTypeId] ) = ( SELECT COUNT(*) FROM [StepType] AS [t] WHERE [t].[StepProgramId] = [st].[StepProgramId] AND [t].[IsActive] = 1 )
),
[Completions] AS (
    SELECT [c].[Id], [c].[StepProgramId], [c].[PersonAliasId], [c].[StartDateTime], [c].[EndDateTime], [c].[CampusId], [c].[StartDateKey]
    FROM [StepProgramCompletion] AS [c]
    WHERE [c].[StepProgramId] IN ( SELECT [Id] FROM [Programs] ) AND [c].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [PeopleWithAllSteps] AS [w] WHERE NOT EXISTS (
            SELECT 1 FROM [Completions] AS [c]
            WHERE [c].[StepProgramId] = [w].[StepProgramId] AND [c].[PersonAliasId] = [w].[PersonAliasId] AND [c].[StartDateTime] = [w].[StartDateTime]
                AND ISNULL( [c].[EndDateTime], '9999-01-01' ) = ISNULL( [w].[EndDateTime], '9999-01-01' )
                AND ISNULL( [c].[CampusId], 0 ) = ISNULL( [w].[CampusId], 0 ) AND [c].[StartDateKey] = CONVERT( INT, FORMAT( [w].[StartDateTime], 'yyyyMMdd' ) ) ) )
    + ( SELECT COUNT(*) FROM [Completions] AS [c] WHERE NOT EXISTS ( SELECT 1 FROM [PeopleWithAllSteps] AS [w] WHERE [w].[StepProgramId] = [c].[StepProgramId] AND [w].[PersonAliasId] = [c].[PersonAliasId] ) )
    + ( SELECT COUNT(*) FROM ( SELECT [StepProgramId], [PersonAliasId] FROM [Completions] GROUP BY [StepProgramId], [PersonAliasId] HAVING COUNT(*) > 1 ) AS [x] )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        LEFT JOIN [Completions] AS [c] ON [c].[StepProgramId] = [st].[StepProgramId] AND [c].[PersonAliasId] = [s].[PersonAliasId]
        WHERE [st].[StepProgramId] IN ( SELECT [Id] FROM [Programs] ) AND [s].[ForeignKey] = @ForeignKey
            AND ISNULL( [s].[StepProgramCompletionId], 0 ) <> ISNULL( [c].[Id], 0 ) );

-- check: people-match-the-dataset-card
-- why: Rock's installer shows the people count from datasets/{key}.json (@CardPeople). Returns how many people the load is off by when that is more than 2%.
SELECT CASE WHEN @CardPeople IS NOT NULL AND ABS( [c].[People] - @CardPeople ) > @CardPeople * 0.02 THEN ABS( [c].[People] - @CardPeople ) ELSE 0 END
FROM ( SELECT COUNT(*) AS [People] FROM [Person] AS [p] WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) ) AS [c];

-- check: households-match-the-dataset-card
-- why: Rock's installer shows the household count from datasets/{key}.json (@CardHouseholds). Returns how many households the load is off by when that is more than 2%.
SELECT CASE WHEN @CardHouseholds IS NOT NULL AND ABS( [c].[Households] - @CardHouseholds ) > @CardHouseholds * 0.02 THEN ABS( [c].[Households] - @CardHouseholds ) ELSE 0 END
FROM (
    SELECT COUNT(*) AS [Households]
    FROM [Group] AS [g]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    WHERE [g].[ForeignKey] = @ForeignKey AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' ) AS [c];

-- check: student-step-belongs-to-a-student
-- why: The Student Path follows student check-ins (decision 115). Every Student Path step belongs to someone who was in grades 7 to 12 on its start and had checked in to the student service by then. Mission Trip is for high school students, and Student Leader for juniors and seniors. A student's grade comes from their graduation year and moves up each June 1, as check-in's does.
WITH [StudentSteps] AS (
    SELECT [st].[Guid] AS [StepTypeGuid], [s].[StartDateTime], [pa].[PersonId], [p].[GraduationYear],
        12 - ( [p].[GraduationYear] - CASE WHEN [s].[StartDateTime] >= DATEFROMPARTS( YEAR( [s].[StartDateTime] ), 6, 1 ) THEN YEAR( [s].[StartDateTime] ) + 1 ELSE YEAR( [s].[StartDateTime] ) END ) AS [Grade]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [StepProgram] AS [sp] ON [sp].[Id] = [st].[StepProgramId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [sp].[Guid] = 'C8AD9B16-5132-4ACB-954B-1E61C734AA67' AND [s].[ForeignKey] = @ForeignKey
)
SELECT COUNT(*)
FROM [StudentSteps] AS [x]
WHERE [x].[GraduationYear] IS NULL
    OR [x].[Grade] > 12
    OR [x].[Grade] < CASE [x].[StepTypeGuid] WHEN '8464EF05-F8E2-4DB3-9AC4-BC1EEE18CBC9' THEN 9 WHEN '724C69AF-20BC-4299-8FD5-A84ED2B4D42D' THEN 11 ELSE 7 END
    OR NOT EXISTS (
        SELECT 1
        FROM [Attendance] AS [a]
        INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
        INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
        INNER JOIN [PersonAlias] AS [apa] ON [apa].[Id] = [a].[PersonAliasId]
        WHERE [apa].[PersonId] = [x].[PersonId] AND [a].[DidAttend] = 1 AND [o].[OccurrenceDate] <= [x].[StartDateTime]
            AND [g].[Name] IN ( 'Grades 7-8', 'Grades 9-12' ) AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' ) ) );

-- check: student-path-decisions-and-leaders
-- why: Follow Jesus is a decision made once. Student Leader is held once, and ends at graduation in May of the student's graduation year, or stays open while they are still in school.
SELECT ( SELECT COUNT(*)
        FROM ( SELECT [s].[PersonAliasId]
            FROM [Step] AS [s]
            INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
            WHERE [st].[Guid] IN ( 'FAC9E970-7434-4067-9E1C-180043CE6FCB', '724C69AF-20BC-4299-8FD5-A84ED2B4D42D' ) AND [s].[ForeignKey] = @ForeignKey
            GROUP BY [s].[PersonAliasId], [st].[Guid]
            HAVING COUNT(*) > 1 ) AS [x] )
    + ( SELECT COUNT(*)
        FROM [Step] AS [s]
        INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
        INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
        WHERE [st].[Guid] = '724C69AF-20BC-4299-8FD5-A84ED2B4D42D' AND [s].[ForeignKey] = @ForeignKey
            AND ( ( [s].[EndDateTime] IS NOT NULL AND ( YEAR( [s].[EndDateTime] ) <> [p].[GraduationYear] OR MONTH( [s].[EndDateTime] ) <> 5 ) )
                OR ( [s].[EndDateTime] IS NULL AND DATEFROMPARTS( [p].[GraduationYear], 6, 1 ) <= CAST( GETDATE() AS DATE ) ) ) );

-- check: student-baptism-follows-the-student-service
-- why: A student's baptism goes on the Discipleship Path's Baptism step (decision 87). A child is baptized here, on a Sunday, after they started coming to the student service. The Baptism Date and Baptized Here agree with the step (checked above).
SELECT COUNT(*)
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
WHERE [st].[Guid] = '801CC43C-0641-4271-939E-75E428F31D06' AND [s].[ForeignKey] = @ForeignKey
    AND EXISTS (
        SELECT 1
        FROM [GroupMember] AS [gm]
        INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
        WHERE [gm].[PersonId] = [pa].[PersonId] AND [r].[Guid] = 'C8B1814F-6AA7-4055-B2D7-48FE20429CB9' )
    AND ( DATEDIFF( DAY, '1900-01-07', [s].[StartDateTime] ) % 7 <> 0
        OR NOT EXISTS (
            SELECT 1
            FROM [Attendance] AS [a]
            INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
            INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
            INNER JOIN [PersonAlias] AS [apa] ON [apa].[Id] = [a].[PersonAliasId]
            WHERE [apa].[PersonId] = [pa].[PersonId] AND [a].[DidAttend] = 1 AND [o].[OccurrenceDate] <= [s].[StartDateTime]
                AND [g].[Name] IN ( 'Grades 7-8', 'Grades 9-12' ) AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' ) ) )
        OR NOT EXISTS (
            SELECT 1
            FROM [AttributeValue] AS [av]
            INNER JOIN [Attribute] AS [at] ON [at].[Id] = [av].[AttributeId]
            WHERE [at].[Guid] = '343A557E-92CC-4A75-B251-0DA732F5EB4E' AND [av].[EntityId] = [s].[Id] AND [av].[Value] = 'True' ) );

-- check: young-kid-checks-in-with-the-family
-- why: Kids from the nursery through 6th grade come only with their family (decision 146). Each of their check-ins is made by an adult of one of their families who was in the worship service that Sunday. A child whose families have no living, active adult checks in on their own, as before.
SELECT COUNT(*)
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[ForeignKey] = @ForeignKey AND [a].[DidAttend] = 1 AND [a].[AttendanceCodeId] IS NOT NULL
    AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( 'CADB2D12-7836-44BC-8EEA-3C6AB22FD5E8', 'E3C8F7D6-5CEB-43BB-802F-66C3E734049E' ) )
    AND EXISTS (
        SELECT 1
        FROM [GroupMember] AS [kid]
        INNER JOIN [GroupMember] AS [adult] ON [adult].[GroupId] = [kid].[GroupId]
        INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [adult].[GroupRoleId]
        INNER JOIN [Person] AS [ap] ON [ap].[Id] = [adult].[PersonId]
        WHERE [kid].[PersonId] = [pa].[PersonId] AND [kid].[GroupTypeId] = [adult].[GroupTypeId]
            AND [r].[Guid] = '2639F9A5-2AAE-4E48-A8C3-4FFE86681E42'
            AND [ap].[IsDeceased] = 0 AND [ap].[RecordStatusValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' ) )
    AND NOT EXISTS (
        SELECT 1
        FROM [PersonAlias] AS [byAlias]
        INNER JOIN [GroupMember] AS [byMember] ON [byMember].[PersonId] = [byAlias].[PersonId]
        INNER JOIN [GroupTypeRole] AS [byRole] ON [byRole].[Id] = [byMember].[GroupRoleId]
        INNER JOIN [GroupMember] AS [kid] ON [kid].[GroupId] = [byMember].[GroupId] AND [kid].[PersonId] = [pa].[PersonId]
        INNER JOIN [PersonAlias] AS [wpa] ON [wpa].[PersonId] = [byAlias].[PersonId]
        INNER JOIN [Attendance] AS [w] ON [w].[PersonAliasId] = [wpa].[Id] AND [w].[DidAttend] = 1
        INNER JOIN [AttendanceOccurrence] AS [wo] ON [wo].[Id] = [w].[OccurrenceId] AND [wo].[SundayDate] = [o].[SundayDate]
        INNER JOIN [Group] AS [wg] ON [wg].[Id] = [wo].[GroupId]
        INNER JOIN [GroupType] AS [wgt] ON [wgt].[Id] = [wg].[GroupTypeId]
        WHERE [byAlias].[Id] = [a].[CheckedInByPersonAliasId]
            AND [byRole].[Guid] = '2639F9A5-2AAE-4E48-A8C3-4FFE86681E42'
            AND [wg].[Name] = 'Weekend Service' AND [wgt].[Guid] = '235BAE2B-5760-4763-AADF-3938F34BA100' );

-- check: onsite-gift-on-a-family-sunday
-- why: Checks and cash go in the offering only on a Sunday the family came (decision 146). A student who comes on their own does not make it a family Sunday, so every gift in a Sunday Offering batch falls on a Sunday when an adult of the giver's family attended.
SELECT COUNT(*)
FROM [FinancialTransaction] AS [t]
INNER JOIN [FinancialBatch] AS [b] ON [b].[Id] = [t].[BatchId]
INNER JOIN [PersonAlias] AS [gpa] ON [gpa].[Id] = [t].[AuthorizedPersonAliasId]
WHERE [t].[ForeignKey] = @ForeignKey AND [b].[Name] LIKE 'Sunday Offering %'
    AND NOT EXISTS (
        SELECT 1
        FROM [GroupMember] AS [giver]
        INNER JOIN [GroupMember] AS [adult] ON [adult].[GroupId] = [giver].[GroupId]
        INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [adult].[GroupRoleId]
        INNER JOIN [PersonAlias] AS [apa] ON [apa].[PersonId] = [adult].[PersonId]
        INNER JOIN [Attendance] AS [a] ON [a].[PersonAliasId] = [apa].[Id] AND [a].[DidAttend] = 1
        INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
        WHERE [giver].[PersonId] = [gpa].[PersonId] AND [giver].[GroupTypeId] = [adult].[GroupTypeId]
            AND [giver].[GroupTypeId] = ( SELECT [Id] FROM [GroupType] WHERE [Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E' )
            AND [r].[Guid] = '2639F9A5-2AAE-4E48-A8C3-4FFE86681E42'
            AND [o].[SundayDate] = CAST( [t].[TransactionDateTime] AS DATE ) );

-- check: student-check-in-fits-the-grade
-- why: Only students in grades 7 to 12 check in to the student service, in the room for their grade on that day. A recent graduate's student check-ins end when they graduate (decision 148). The grade comes from the graduation year and moves up each June 1, as check-in's does.
SELECT COUNT(*)
FROM (
    SELECT [g].[Name],
        12 - ( [p].[GraduationYear] - CASE WHEN [o].[OccurrenceDate] >= DATEFROMPARTS( YEAR( [o].[OccurrenceDate] ), 6, 1 ) THEN YEAR( [o].[OccurrenceDate] ) + 1 ELSE YEAR( [o].[OccurrenceDate] ) END ) AS [Grade]
    FROM [Attendance] AS [a]
    INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [a].[ForeignKey] = @ForeignKey AND [a].[DidAttend] = 1
        AND [g].[Name] IN ( 'Grades 7-8', 'Grades 9-12' ) AND [g].[GroupTypeId] IN ( SELECT [Id] FROM [GroupType] WHERE [Guid] IN ( '7A17235B-69AD-439B-BAB0-1A0A472DB96F', '9A88743B-F336-4404-B877-2A623689195D' ) )
) AS [x]
WHERE [x].[Grade] IS NULL
    OR ( [x].[Name] = 'Grades 7-8' AND [x].[Grade] NOT BETWEEN 7 AND 8 )
    OR ( [x].[Name] = 'Grades 9-12' AND [x].[Grade] NOT BETWEEN 9 AND 12 );

-- check: campus-account-dates-match-fund
-- why: Rock's giving pages hide an account outside its dates. A campus account with other dates than its fund would take gifts the fund says it cannot.
SELECT COUNT(*)
FROM [FinancialAccount] AS [child]
INNER JOIN [FinancialAccount] AS [fund] ON [fund].[Id] = [child].[ParentAccountId]
WHERE [child].[ForeignKey] = @ForeignKey AND [child].[CampusId] IS NOT NULL
    AND ( ISNULL( [child].[StartDate], '1900-01-01' ) <> ISNULL( [fund].[StartDate], '1900-01-01' )
        OR ISNULL( [child].[EndDate], '1900-01-01' ) <> ISNULL( [fund].[EndDate], '1900-01-01' ) );

-- check: pledge-is-on-a-top-level-fund
-- why: Rock's Pledge Analytics and statements count gifts to the pledged account and its child accounts. A pledge on a campus account would miss gifts to the fund's other campus accounts.
SELECT COUNT(*)
FROM [FinancialPledge] AS [fp]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [fp].[AccountId]
WHERE [fp].[ForeignKey] = @ForeignKey AND [fa].[ParentAccountId] IS NOT NULL;

-- check: pledge-dates-fit-the-fund
-- why: Rock saves the date keys from the dates, and a pledge runs inside its fund's dates. Pledge Analytics and statements filter on both.
SELECT COUNT(*)
FROM [FinancialPledge] AS [fp]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [fp].[AccountId]
WHERE [fp].[ForeignKey] = @ForeignKey
    AND ( [fp].[StartDate] > [fp].[EndDate]
        OR [fp].[StartDateKey] <> CONVERT( INT, FORMAT( [fp].[StartDate], 'yyyyMMdd' ) )
        OR [fp].[EndDateKey] <> CONVERT( INT, FORMAT( [fp].[EndDate], 'yyyyMMdd' ) )
        OR [fp].[StartDate] < ISNULL( [fa].[StartDate], [fp].[StartDate] )
        OR [fp].[EndDate] > ISNULL( [fa].[EndDate], [fp].[EndDate] ) );

-- check: one-pledge-per-giving-unit-and-fund
-- why: Pledge Analytics adds up pledges by giving unit. Two pledges to one fund from one unit would double the amount it shows as pledged.
SELECT COUNT(*)
FROM (
    SELECT [p].[GivingId], [fp].[AccountId]
    FROM [FinancialPledge] AS [fp]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [fp].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [fp].[ForeignKey] = @ForeignKey
    GROUP BY [p].[GivingId], [fp].[AccountId]
    HAVING COUNT(*) > 1
) AS [x];

-- check: gift-posts-to-campus-account
-- why: A fund with campus accounts takes gifts only through them, so campus giving reports add up. A gift to the fund itself would count toward no campus.
SELECT COUNT(*)
FROM [FinancialTransactionDetail] AS [ftd]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [ftd].[AccountId]
WHERE [ftd].[ForeignKey] = @ForeignKey
    AND EXISTS ( SELECT 1 FROM [FinancialAccount] AS [child] WHERE [child].[ParentAccountId] = [fa].[Id] AND [child].[CampusId] IS NOT NULL );

-- check: communication-list-members-allow-mass-email
-- why: Decision 133. Nobody who chose "No mass emails" or "Do not email" is on a communication list, so a list send never reaches someone who asked not to get one.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gt].[Guid] = 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' AND [g].[ForeignKey] = @ForeignKey
    AND [gm].[GroupMemberStatus] = 1 AND [p].[EmailPreference] <> 0;

-- check: newsletter-members-belong-to-the-campus
-- why: A campus newsletter holds that campus's adults with an active email (decision 133), so a campus's news never goes to another campus. An email that bounced after the person joined carries Rock's bounce note.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
WHERE [gt].[Guid] = 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' AND [g].[ForeignKey] = @ForeignKey AND [g].[CampusId] IS NOT NULL
    AND ( [p].[PrimaryCampusId] <> [g].[CampusId] OR ISNULL( [p].[Email], '' ) = '' OR [p].[AgeClassification] <> 1
        OR ( [p].[IsEmailActive] = 0 AND ISNULL( [p].[EmailNote], '' ) NOT LIKE 'Email experienced a Hard Bounce%' ) );

-- check: volunteers-list-follows-the-teams
-- why: The Volunteers list is everyone active on a serving team (decision 92), so a message to volunteers reaches exactly the people who serve. A volunteer who left the list from one of its emails stays on it as Inactive.
WITH [Serving] AS (
    SELECT DISTINCT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
    WHERE [gt].[Guid] = '2C42B2D4-1C5F-4AD5-A9AD-08631B872AC4' AND ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
        AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 AND [p].[EmailPreference] = 0
),
[Listed] AS (
    SELECT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    WHERE [g].[Guid] = '7C040D39-B8F0-481E-95B3-9526D566C47F' AND [g].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [Serving] AS [s] WHERE NOT EXISTS ( SELECT 1 FROM [Listed] AS [l] WHERE [l].[PersonId] = [s].[PersonId] ) )
    + ( SELECT COUNT(*) FROM [Listed] AS [l] WHERE NOT EXISTS ( SELECT 1 FROM [Serving] AS [s] WHERE [s].[PersonId] = [l].[PersonId] ) );

-- check: small-group-leaders-list-follows-the-groups
-- why: The Small Group Leaders list is everyone who leads an active small group (decision 92), so leader messages reach exactly the leaders. A leader who left the list from one of its emails stays on it as Inactive.
WITH [Leading] AS (
    SELECT DISTINCT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId]
    WHERE [gt].[Guid] = '50FCFB30-F51A-49DF-86F4-2B176EA1820B' AND ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
        AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0 AND [r].[IsLeader] = 1 AND [p].[EmailPreference] = 0
),
[Listed] AS (
    SELECT [gm].[PersonId]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    WHERE [g].[Guid] = '3D755601-598D-4E8F-A13D-9DF0F0087CA9' AND [g].[ForeignKey] = @ForeignKey
)
SELECT ( SELECT COUNT(*) FROM [Leading] AS [x] WHERE NOT EXISTS ( SELECT 1 FROM [Listed] AS [l] WHERE [l].[PersonId] = [x].[PersonId] ) )
    + ( SELECT COUNT(*) FROM [Listed] AS [l] WHERE NOT EXISTS ( SELECT 1 FROM [Leading] AS [x] WHERE [x].[PersonId] = [l].[PersonId] ) );

-- check: group-sync-targets-a-sync-group
-- why: Decision 186. Rock's group detail shows the sync panel only on group types that allow group sync, and a sync's role must be one of its group's roles, so every sync opens and edits in Rock. Its data view is a person view, as Rock's sync requires.
SELECT COUNT(*)
FROM [GroupSync] AS [s]
INNER JOIN [Group] AS [g] ON [g].[Id] = [s].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [s].[GroupTypeRoleId]
INNER JOIN [DataView] AS [dv] ON [dv].[Id] = [s].[SyncDataViewId]
WHERE [s].[ForeignKey] = @ForeignKey
    AND ( [gt].[AllowGroupSync] = 0 OR [r].[GroupTypeId] <> [g].[GroupTypeId]
        OR [dv].[EntityTypeId] <> ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person' ) );

-- check: group-sync-refreshed-at-install
-- why: Decision 187. Each sync is written as the job leaves it after a run on install day, so Rock's group detail shows when it last ran and a sync with an interval waits for it.
SELECT COUNT(*)
FROM [GroupSync]
WHERE [ForeignKey] = @ForeignKey AND ( [LastRefreshDateTime] IS NULL OR [LastRefreshDateTime] < [CreatedDateTime] );

-- check: church-data-view-is-filed
-- why: Decision 185. Every data view the church adds sits in a child of the church's own Rock Solid Church category, so it never mixes with the views Rock ships, and later views have a place to go.
SELECT COUNT(*)
FROM [DataView] AS [dv]
LEFT JOIN [Category] AS [c] ON [c].[Id] = [dv].[CategoryId]
LEFT JOIN [Category] AS [parent] ON [parent].[Id] = [c].[ParentCategoryId]
WHERE [dv].[ForeignKey] = @ForeignKey
    AND ( [c].[Id] IS NULL OR ISNULL( [c].[ForeignKey], '' ) <> @ForeignKey OR ISNULL( [parent].[ForeignKey], '' ) <> @ForeignKey );

-- check: data-view-filter-tree-is-whole
-- why: Rock reads a view's filters from its root down. Every church view has a root filter, every filter belongs to its view, a group of filters names no component, and each single filter names its component and selection.
SELECT ( SELECT COUNT(*) FROM [DataView] WHERE [ForeignKey] = @ForeignKey AND [DataViewFilterId] IS NULL )
    + ( SELECT COUNT(*)
        FROM [DataViewFilter] AS [f]
        WHERE [f].[ForeignKey] = @ForeignKey
            AND ( [f].[DataViewId] IS NULL
                OR ( [f].[ExpressionType] = 0 AND ( [f].[EntityTypeId] IS NULL OR ISNULL( [f].[Selection], '' ) = '' ) )
                OR ( [f].[ExpressionType] <> 0 AND [f].[EntityTypeId] IS NOT NULL )
                OR ( [f].[ParentId] IS NULL AND NOT EXISTS ( SELECT 1 FROM [DataView] AS [dv] WHERE [dv].[DataViewFilterId] = [f].[Id] ) ) ) );

-- check: system-phone-numbers-are-reserved-and-unused
-- why: Content standard 2 and decision 93. The church's texting numbers are active, can text, use the reserved range, and are never a loaded person's number, so SMS Conversations never seems to text a member from their own phone.
SELECT COUNT(*)
FROM [SystemPhoneNumber] AS [spn]
WHERE [spn].[ForeignKey] = @ForeignKey
    AND ( [spn].[IsActive] = 0 OR [spn].[IsSmsEnabled] = 0 OR [spn].[IsSmsForwardingEnabled] = 1
        OR LEN( [spn].[Number] ) <> 12 OR LEFT( [spn].[Number], 2 ) <> '+1' OR SUBSTRING( [spn].[Number], 3, 3 ) NOT IN ( '602', '480', '623' ) OR SUBSTRING( [spn].[Number], 6, 5 ) <> '55501'
        OR EXISTS ( SELECT 1 FROM [PhoneNumber] AS [pn] INNER JOIN [Person] AS [p] ON [p].[Id] = [pn].[PersonId] WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) AND [pn].[Number] = SUBSTRING( [spn].[Number], 3, 10 ) ) );

-- check: communication-is-sent-and-never-sends-again
-- why: Decision 95. Rock's send job picks up any communication that is not Approved with a send time, so every sample communication is Approved, has its send time, and has the date key Rock fills from it.
SELECT COUNT(*)
FROM [Communication] AS [c]
WHERE [c].[ForeignKey] = @ForeignKey
    AND ( [c].[Status] <> 3 OR [c].[SendDateTime] IS NULL OR [c].[SendDateKey] IS NULL
        OR [c].[SendDateKey] <> CONVERT( INT, CONVERT( CHAR(8), [c].[SendDateTime], 112 ) ) );

-- check: no-recipient-waits-to-send
-- why: Decision 95. A Pending or Sending recipient is one Rock's send job would still send to.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
WHERE [cr].[ForeignKey] = @ForeignKey AND [cr].[Status] IN ( 0, 5 );

-- check: email-recipient-states-agree
-- why: Communication Detail counts and charts each status from these columns. A delivered or opened email was sent and confirmed, an opened one has its first open after the send, and a failed one says why. Only a bounce fails after Rock sent it.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 1
    AND ( ( [cr].[Status] IN ( 1, 4 ) AND ( [cr].[SendDateTime] IS NULL OR [cr].[DeliveredDateTime] IS NULL OR [cr].[DeliveredDateTime] < [cr].[SendDateTime] ) )
        OR ( [cr].[Status] = 4 AND ( [cr].[OpenedDateTime] IS NULL OR [cr].[OpenedDateTime] < [cr].[SendDateTime] OR [cr].[OpenedClient] IS NULL ) )
        OR ( [cr].[Status] <> 4 AND [cr].[OpenedDateTime] IS NOT NULL )
        OR ( [cr].[Status] = 2 AND ( [cr].[StatusNote] IS NULL OR ( [cr].[SendDateTime] IS NOT NULL AND [cr].[StatusNote] NOT LIKE '5__ 5.%' ) ) ) );

-- check: opens-and-clicks-follow-an-open
-- why: Rock records an open or a click as an interaction on the communication's component, pointing at the recipient. Only an opened recipient has them, the first is an open at the recipient's open time, and nothing comes before the send.
SELECT COUNT(*)
FROM [Interaction] AS [i]
INNER JOIN [InteractionComponent] AS [ic] ON [ic].[Id] = [i].[InteractionComponentId]
INNER JOIN [InteractionChannel] AS [ch] ON [ch].[Id] = [ic].[InteractionChannelId]
LEFT JOIN [CommunicationRecipient] AS [cr] ON [cr].[Id] = [i].[EntityId] AND [cr].[CommunicationId] = [ic].[EntityId]
WHERE [ch].[Guid] = 'C88A187F-0343-4E7C-AF3F-79A8989DFA65' AND [i].[ForeignKey] = @ForeignKey
    AND ( [cr].[Id] IS NULL OR [cr].[Status] <> 4 OR [i].[InteractionDateTime] < [cr].[OpenedDateTime] OR [i].[PersonAliasId] <> [cr].[PersonAliasId]
        OR ( [i].[Operation] = 'Click' AND ISNULL( [i].[InteractionData], '' ) NOT LIKE 'https://rocksolidchurchdemo.com/%' ) );

-- check: unsubscribe-matches-the-person
-- why: Decision 131. A list unsubscribe leaves the person Inactive in that list, a Bulk one leaves them on "No mass emails" or stricter, and Rock records a level with every unsubscribe time.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [cr].[ForeignKey] = @ForeignKey
    AND ( ( [cr].[UnsubscribeDateTime] IS NULL AND [cr].[UnsubscribeLevel] IS NOT NULL ) OR ( [cr].[UnsubscribeDateTime] IS NOT NULL AND [cr].[UnsubscribeLevel] IS NULL )
        OR ( [cr].[UnsubscribeLevel] = 3 AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] WHERE [gm].[GroupId] = [c].[ListGroupId] AND [gm].[PersonId] = [p].[Id] AND [gm].[GroupMemberStatus] = 0 ) )
        OR ( [cr].[UnsubscribeLevel] = 2 AND [p].[EmailPreference] = 0 ) );

-- check: sent-email-has-its-history
-- why: Rock's email transport writes a "Sent" line on the person's History tab for every email it sends, so every sent email recipient has one.
SELECT ABS( ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
        WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 1 AND [cr].[SendDateTime] IS NOT NULL )
    - ( SELECT COUNT(*) FROM [History] AS [h] WHERE [h].[ForeignKey] = @ForeignKey AND [h].[Verb] = 'SENT' AND [h].[ValueName] = 'Communication' ) );

-- check: serving-email-follows-a-request
-- why: Decision 159. Rock's Send Group Schedule Notifications job sends a confirmation the day a volunteer is asked to serve, and a reminder two days before a Sunday they did not decline. Each serving email has one recipient and the request it came from.
WITH [Serving] AS (
    SELECT [c].[Id], [c].[SendDateTime], [sc].[Guid] AS [Kind]
    FROM [Communication] AS [c]
    INNER JOIN [SystemCommunication] AS [sc] ON [sc].[Id] = [c].[SystemCommunicationId]
    WHERE [c].[ForeignKey] = @ForeignKey AND [sc].[Guid] IN ( 'BA1716E0-6B31-4E93-ABA1-42B3C81FDBDC', '8A20FE79-B73C-447A-82B1-416F9B50C038' )
), [Sent] AS (
    SELECT [s].[Kind], [pa].[PersonId], CONVERT( DATE, [s].[SendDateTime] ) AS [Day], COUNT(*) AS [Recipients]
    FROM [Serving] AS [s]
    INNER JOIN [CommunicationRecipient] AS [cr] ON [cr].[CommunicationId] = [s].[Id]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
    GROUP BY [s].[Id], [s].[Kind], [pa].[PersonId], CONVERT( DATE, [s].[SendDateTime] )
), [Asked] AS (
    SELECT DISTINCT [pa].[PersonId], CONVERT( DATE, [a].[CreatedDateTime] ) AS [Day]
    FROM [Attendance] AS [a]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    WHERE [a].[RequestedToAttend] = 1 AND [a].[ScheduleConfirmationSent] = 1
), [Reminded] AS (
    SELECT DISTINCT [pa].[PersonId], CONVERT( DATE, DATEADD( DAY, -2, [a].[StartDateTime] ) ) AS [Day]
    FROM [Attendance] AS [a]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    WHERE [a].[RequestedToAttend] = 1 AND [a].[ScheduleReminderSent] = 1 AND ISNULL( [a].[RSVP], 3 ) <> 0
)
SELECT ( SELECT COUNT(*) FROM [Serving] WHERE NOT EXISTS ( SELECT 1 FROM [CommunicationRecipient] AS [cr] WHERE [cr].[CommunicationId] = [Serving].[Id] ) )
    + ( SELECT COUNT(*) FROM [Sent] AS [s]
        WHERE [s].[Recipients] <> 1
            OR ( [s].[Kind] = 'BA1716E0-6B31-4E93-ABA1-42B3C81FDBDC' AND NOT EXISTS ( SELECT 1 FROM [Asked] AS [q] WHERE [q].[PersonId] = [s].[PersonId] AND [q].[Day] = [s].[Day] ) )
            OR ( [s].[Kind] = '8A20FE79-B73C-447A-82B1-416F9B50C038' AND NOT EXISTS ( SELECT 1 FROM [Reminded] AS [r] WHERE [r].[PersonId] = [s].[PersonId] AND [r].[Day] = [s].[Day] ) ) );

-- check: serving-request-is-not-in-the-future
-- why: Decision 165. A request to serve, its answer, and its last change all happened by install day, even for a Sunday weeks ahead.
SELECT COUNT(*)
FROM [Attendance] AS [a]
WHERE [a].[ForeignKey] = @ForeignKey AND [a].[RequestedToAttend] = 1
    AND ( [a].[CreatedDateTime] > GETDATE() OR [a].[RSVPDateTime] > GETDATE() OR [a].[ModifiedDateTime] > GETDATE() );

-- check: serving-flag-has-its-email
-- why: Decision 165. Rock's scheduling job marks a confirmation or a reminder sent only when it emailed the volunteer, and it reminded only volunteers who confirmed. In the 2 years of communication history before install day, each flag has its email.
WITH [Window] AS (
    SELECT DATEADD( DAY, 1, DATEADD( YEAR, -2, CONVERT( DATE, GETDATE() ) ) ) AS [Start], CONVERT( DATE, GETDATE() ) AS [Today]
), [Emailed] AS (
    SELECT DISTINCT [sc].[Guid] AS [Kind], [pa].[PersonId], CONVERT( DATE, [c].[SendDateTime] ) AS [Day]
    FROM [Communication] AS [c]
    INNER JOIN [SystemCommunication] AS [sc] ON [sc].[Id] = [c].[SystemCommunicationId]
    INNER JOIN [CommunicationRecipient] AS [cr] ON [cr].[CommunicationId] = [c].[Id]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
    WHERE [c].[ForeignKey] = @ForeignKey AND [sc].[Guid] IN ( 'BA1716E0-6B31-4E93-ABA1-42B3C81FDBDC', '8A20FE79-B73C-447A-82B1-416F9B50C038' )
), [Flagged] AS (
    SELECT [pa].[PersonId], [a].[RSVP], [a].[ScheduleConfirmationSent], [a].[ScheduleReminderSent], [a].[StartDateTime],
        CONVERT( DATE, [a].[CreatedDateTime] ) AS [AskedDay], CONVERT( DATE, DATEADD( DAY, -ISNULL( [gt].[ScheduleReminderEmailOffsetDays], 2 ), [a].[StartDateTime] ) ) AS [ReminderDay]
    FROM [Attendance] AS [a]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    INNER JOIN [AttendanceOccurrence] AS [ao] ON [ao].[Id] = [a].[OccurrenceId]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [ao].[GroupId]
    INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
    WHERE [a].[ForeignKey] = @ForeignKey AND [a].[RequestedToAttend] = 1 AND ( [a].[ScheduleConfirmationSent] = 1 OR [a].[ScheduleReminderSent] = 1 )
)
SELECT COUNT(*)
FROM [Flagged] AS [f]
CROSS JOIN [Window] AS [w]
WHERE ( [f].[ScheduleReminderSent] = 1 AND [f].[ReminderDay] < [w].[Today] AND ISNULL( [f].[RSVP], 3 ) <> 1 )
    OR ( [f].[ScheduleConfirmationSent] = 1 AND [f].[AskedDay] >= [w].[Start] AND [f].[AskedDay] < [w].[Today]
        AND NOT EXISTS ( SELECT 1 FROM [Emailed] AS [e] WHERE [e].[Kind] = 'BA1716E0-6B31-4E93-ABA1-42B3C81FDBDC' AND [e].[PersonId] = [f].[PersonId] AND [e].[Day] = [f].[AskedDay] ) )
    OR ( [f].[ScheduleReminderSent] = 1 AND [f].[ReminderDay] >= [w].[Start] AND [f].[ReminderDay] < [w].[Today]
        AND NOT EXISTS ( SELECT 1 FROM [Emailed] AS [e] WHERE [e].[Kind] = '8A20FE79-B73C-447A-82B1-416F9B50C038' AND [e].[PersonId] = [f].[PersonId] AND [e].[Day] = [f].[ReminderDay] ) );

-- check: serving-job-has-nothing-to-send
-- why: Decision 95. Rock's scheduling job emails any volunteer it can reach whose request for a coming Sunday is not yet marked confirmed, and reminds confirmed volunteers not yet reminded, so every such request is marked.
SELECT COUNT(*)
FROM [Attendance] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [a].[ForeignKey] = @ForeignKey AND [a].[RequestedToAttend] = 1 AND [a].[StartDateTime] >= CONVERT( DATE, GETDATE() )
    AND ISNULL( [p].[Email], '' ) <> '' AND [p].[IsEmailActive] = 1 AND [p].[EmailPreference] <> 2
    AND ( [a].[ScheduleConfirmationSent] = 0 OR ( [a].[ScheduleReminderSent] = 0 AND [a].[RSVP] = 1 ) );

-- check: one-to-one-email-skips-do-not-email
-- why: Rock sends a personal email to someone on "No mass emails", but never to someone on "Do not email". Every one-to-one email to a person on "Do not email" failed.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 1 AND [c].[IsBulkCommunication] = 0 AND [p].[EmailPreference] = 2
    AND [cr].[Status] <> 2;

-- check: sent-push-is-delivered-with-its-history
-- why: Decision 161. Rock Mobile Push marks a sent push Delivered with its send and delivered times and writes a "Push Notification" line on the person's History. A person with no device that takes notifications fails with Rock's note.
SELECT ( SELECT COUNT(*)
        FROM [CommunicationRecipient] AS [cr]
        INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
        WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 3
            AND ( ( [cr].[Status] IN ( 1, 4 ) AND ( [cr].[SendDateTime] IS NULL OR [cr].[DeliveredDateTime] IS NULL OR [cr].[TransportEntityTypeName] <> 'Rock.Communication.Transport.RockMobilePush' ) )
                OR ( [cr].[Status] = 4 AND ( [cr].[OpenedDateTime] IS NULL OR [cr].[OpenedDateTime] < [cr].[SendDateTime] ) )
                OR ( [cr].[Status] = 2 AND ISNULL( [cr].[StatusNote], '' ) <> 'No Personal Devices with Messaging Enabled' )
                OR [cr].[Status] NOT IN ( 1, 2, 4 ) OR [c].[PushData] IS NULL ) )
    + ABS( ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
            WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 3 AND [cr].[SendDateTime] IS NOT NULL )
        - ( SELECT COUNT(*) FROM [History] AS [h] WHERE [h].[ForeignKey] = @ForeignKey AND [h].[Verb] = 'SENT' AND [h].[ValueName] = 'Push Notification' ) );

-- check: push-goes-to-a-device-that-takes-notifications
-- why: Rock Mobile Push sends to every active device on the person with notifications on and a registration token. A delivered push had one when it was sent, and a failed one had none.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
LEFT JOIN (
    SELECT [pd].[PersonAliasId], MIN( [pd].[CreatedDateTime] ) AS [FirstSeen]
    FROM [PersonalDevice] AS [pd]
    WHERE [pd].[IsActive] = 1 AND [pd].[NotificationsEnabled] = 1 AND ISNULL( [pd].[DeviceRegistrationId], '' ) <> ''
    GROUP BY [pd].[PersonAliasId]
) AS [d] ON [d].[PersonAliasId] = [cr].[PersonAliasId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 3
    AND ( CASE WHEN [d].[FirstSeen] <= [c].[SendDateTime] THEN 1 ELSE 0 END ) <> ( CASE WHEN [cr].[Status] IN ( 1, 4 ) THEN 1 ELSE 0 END );

-- check: app-devices-never-reach-a-phone
-- why: Decision 95. Every sample device's registration token starts with "sample-data-", which Firebase never issues, so a push sent from a sample database can never reach a real phone.
SELECT COUNT(*)
FROM [PersonalDevice] AS [pd]
WHERE [pd].[ForeignKey] = @ForeignKey AND ISNULL( [pd].[DeviceRegistrationId], '' ) NOT LIKE 'sample-data-%';

-- check: no-active-flow-instance
-- why: Decisions 95 and 96. Rock's Process Communication Flows job sends the next message of any instance that is not complete, so every sample flow instance is complete and each of its emails already went out.
SELECT ( SELECT COUNT(*) FROM [CommunicationFlowInstance] AS [i] WHERE [i].[ForeignKey] = @ForeignKey AND [i].[IsMessagingCompleted] = 0 )
    + ( SELECT COUNT(*)
        FROM [CommunicationFlowInstanceCommunication] AS [ic]
        INNER JOIN [Communication] AS [c] ON [c].[Id] = [ic].[CommunicationId]
        WHERE [ic].[ForeignKey] = @ForeignKey AND ( [c].[SendDateTime] IS NULL OR [c].[SendDateTime] > GETDATE() ) );

-- check: flow-email-reaches-its-instance
-- why: Decision 96. The job writes each flow message as a bulk email from its message's flow template, to exactly the instance's recipients.
SELECT COUNT(*)
FROM [CommunicationFlowInstanceCommunication] AS [ic]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [ic].[CommunicationId]
INNER JOIN [CommunicationFlowCommunication] AS [fc] ON [fc].[Id] = [ic].[CommunicationFlowCommunicationId]
INNER JOIN [CommunicationTemplate] AS [t] ON [t].[Id] = [fc].[CommunicationTemplateId]
WHERE [ic].[ForeignKey] = @ForeignKey
    AND ( [c].[CommunicationType] <> 1 OR [c].[IsBulkCommunication] = 0 OR ISNULL( [c].[CommunicationTemplateId], 0 ) <> [t].[Id] OR [t].[UsageType] <> 1 OR [t].[Version] <> 1
        OR ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] WHERE [cr].[CommunicationId] = [c].[Id] )
            <> ( SELECT COUNT(*) FROM [CommunicationFlowInstanceRecipient] AS [r] WHERE [r].[CommunicationFlowInstanceId] = [ic].[CommunicationFlowInstanceId] )
        OR EXISTS ( SELECT 1 FROM [CommunicationRecipient] AS [cr]
            WHERE [cr].[CommunicationId] = [c].[Id]
                AND NOT EXISTS ( SELECT 1 FROM [CommunicationFlowInstanceRecipient] AS [r] WHERE [r].[CommunicationFlowInstanceId] = [ic].[CommunicationFlowInstanceId] AND [r].[RecipientPersonAliasId] = [cr].[PersonAliasId] ) ) );

-- check: sent-text-is-delivered-with-its-text
-- why: SMS Conversations shows an outbound text only when its recipient is Delivered, and reads its words only from SentMessage (decision 93). A text Rock could not send failed with Rock's note and has no send time.
SELECT COUNT(*)
FROM [CommunicationRecipient] AS [cr]
INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 2
    AND ( ( [cr].[SendDateTime] IS NOT NULL AND ( [cr].[Status] <> 1 OR ISNULL( [cr].[SentMessage], '' ) = '' OR [cr].[DeliveredDateTime] IS NULL OR [c].[SmsFromSystemPhoneNumberId] IS NULL ) )
        OR ( [cr].[SendDateTime] IS NULL AND ( [cr].[Status] <> 2 OR [cr].[StatusNote] IS NULL ) ) );

-- check: sent-text-has-its-history
-- why: Rock's Twilio transport writes a "SMS message" line on the person's History tab for every text it sends.
SELECT ABS( ( SELECT COUNT(*) FROM [CommunicationRecipient] AS [cr] INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
        WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 2 AND [cr].[SendDateTime] IS NOT NULL )
    - ( SELECT COUNT(*) FROM [History] AS [h] WHERE [h].[ForeignKey] = @ForeignKey AND [h].[Verb] = 'SENT' AND [h].[ValueName] = 'SMS message' ) );

-- check: texted-stop-turns-off-texting
-- why: Decision 131. A texted STOP marks the person's newest text as a level All unsubscribe and turns off texting on their mobile, and Rock records the opt-out on the phone. A phone opted out in the sample data came from a STOP.
SELECT ( SELECT COUNT(*)
        FROM [CommunicationRecipient] AS [cr]
        INNER JOIN [Communication] AS [c] ON [c].[Id] = [cr].[CommunicationId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
        WHERE [cr].[ForeignKey] = @ForeignKey AND [c].[CommunicationType] = 2 AND [cr].[UnsubscribeLevel] IS NOT NULL
            AND ( [cr].[UnsubscribeLevel] <> 1 OR NOT EXISTS (
                SELECT 1 FROM [PhoneNumber] AS [pn] WHERE [pn].[PersonId] = [pa].[PersonId] AND [pn].[IsMessagingOptedOut] = 1 AND [pn].[IsMessagingEnabled] = 0 AND [pn].[MessagingOptedOutDateTime] IS NOT NULL ) ) )
    + ( SELECT COUNT(*)
        FROM [PhoneNumber] AS [pn]
        INNER JOIN [Person] AS [p] ON [p].[Id] = [pn].[PersonId]
        WHERE ( [p].[ForeignKey] = @ForeignKey OR [p].[Id] = @AdminPersonId ) AND [pn].[IsMessagingOptedOut] = 1
            AND NOT EXISTS ( SELECT 1 FROM [CommunicationResponse] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[FromPersonAliasId]
                WHERE [pa].[PersonId] = [p].[Id] AND [r].[Response] = 'STOP' AND [r].[ForeignKey] = @ForeignKey ) );

-- check: text-reply-comes-from-a-known-phone
-- why: SMS Conversations groups a person's texts by their number, so every text a person sent comes from their own mobile, to one of the church's active texting numbers.
SELECT COUNT(*)
FROM [CommunicationResponse] AS [r]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[FromPersonAliasId]
LEFT JOIN [SystemPhoneNumber] AS [spn] ON [spn].[Id] = [r].[RelatedSmsFromSystemPhoneNumberId]
WHERE [r].[ForeignKey] = @ForeignKey
    AND ( [pa].[Id] IS NULL OR [spn].[Id] IS NULL OR [spn].[IsActive] = 0
        OR NOT EXISTS ( SELECT 1 FROM [PhoneNumber] AS [pn] WHERE [pn].[PersonId] = [pa].[PersonId] AND '1' + [pn].[Number] = [r].[MessageKey] ) );

-- check: open-requests-are-mostly-not-past-due
-- why: A working team's board shows most open requests not due yet (decision 173). Dating due dates from the request again would put nearly every active request past due, so past due stays under a third of them. It is judged only with 50 or more active requests: Core 1 and Core 3 have few, mostly the Cast's, and three Cast stories are past due on purpose.
SELECT CASE WHEN COUNT(*) >= 50 AND SUM( CASE WHEN CAST( [DueDate] AS DATE ) < CAST( GETDATE() AS DATE ) THEN 1 ELSE 0 END ) * 3 > COUNT(*) THEN 1 ELSE 0 END
FROM [ConnectionRequest]
WHERE [ForeignKey] = @ForeignKey AND [ConnectionState] = 0;

-- check: open-request-is-due-within-its-status-time
-- why: An open request's due date runs from its last status change, which is never after install day, so it is never due later than its status's days from today (decision 173).
SELECT COUNT(*)
FROM [ConnectionRequest] AS [cr]
INNER JOIN [ConnectionStatus] AS [cs] ON [cs].[Id] = [cr].[ConnectionStatusId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [cr].[ConnectionState] = 0 AND [cs].[RequestStatusDueDateOffsetInDays] IS NOT NULL
    AND CAST( [cr].[DueDate] AS DATE ) > DATEADD( DAY, [cs].[RequestStatusDueDateOffsetInDays], CAST( GETDATE() AS DATE ) );

-- check: event-occurrence-is-listed
-- why: Rock's calendar pages and the app list only active, approved events on a calendar. An occurrence of any other event never shows (decision 174).
SELECT COUNT(*)
FROM [EventItemOccurrence] AS [o]
INNER JOIN [EventItem] AS [e] ON [e].[Id] = [o].[EventItemId]
WHERE [o].[ForeignKey] = @ForeignKey
    AND ( [e].[IsActive] = 0 OR [e].[IsApproved] = 0
        OR NOT EXISTS ( SELECT 1 FROM [EventCalendarItem] AS [ci] WHERE [ci].[EventItemId] = [e].[Id] )
        OR NOT EXISTS ( SELECT 1 FROM [EventItemAudience] AS [a] WHERE [a].[EventItemId] = [e].[Id] ) );

-- check: event-occurrence-has-a-schedule
-- why: Rock builds an occurrence's dates from its schedule's iCalendar text, and its save hook sets the effective start. Without them the event has no dates.
SELECT COUNT(*)
FROM [EventItemOccurrence] AS [o]
LEFT JOIN [Schedule] AS [s] ON [s].[Id] = [o].[ScheduleId]
WHERE [o].[ForeignKey] = @ForeignKey
    AND ( [s].[Id] IS NULL OR [s].[IsActive] = 0 OR ISNULL( [s].[iCalendarContent], '' ) = '' OR [s].[EffectiveStartDate] IS NULL );

-- check: event-occurrence-contact-is-a-person-with-church-email
-- why: The event page shows its contact's name, phone, and email. Content standard 3: the email is the sample church's own domain.
SELECT COUNT(*)
FROM [EventItemOccurrence] AS [o]
LEFT JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [o].[ContactPersonAliasId]
WHERE [o].[ForeignKey] = @ForeignKey
    AND ( [pa].[Id] IS NULL OR [o].[ContactEmail] NOT LIKE '%@rocksolidchurchdemo.com' OR [o].[ContactPhone] NOT LIKE '(%) 555-01__' );

-- check: event-photo-is-a-stored-file
-- why: An event's photo shows on its web and app pages. The id must point at a file whose bytes are in the database.
SELECT COUNT(*)
FROM [EventItem] AS [e]
LEFT JOIN [BinaryFile] AS [f] ON [f].[Id] = [e].[PhotoId]
WHERE [e].[PhotoId] IS NOT NULL
    AND EXISTS ( SELECT 1 FROM [EventItemOccurrence] AS [o] WHERE [o].[EventItemId] = [e].[Id] AND [o].[ForeignKey] = @ForeignKey )
    AND ( [f].[Id] IS NULL OR NOT EXISTS ( SELECT 1 FROM [BinaryFileData] AS [d] WHERE [d].[Id] = [f].[Id] AND DATALENGTH( [d].[Content] ) > 0 ) );

-- check: dataset-has-logins
-- why: Decision 179. The verify tool always loads with a test password, so a load with no logins means logins broke, and every login rule below would pass on nothing.
SELECT CASE WHEN EXISTS ( SELECT 1 FROM [UserLogin] WHERE [ForeignKey] = @ForeignKey ) THEN 0 ELSE 1 END;

-- check: login-is-a-database-login-rock-can-check
-- why: Decision 180. Rock's Database login reads a BCrypt hash ($2a$, $2b$, or $2y$), and a demo person must sign in on the first try: not locked out, and not sent to change their password.
SELECT COUNT(*)
FROM [UserLogin] AS [u]
LEFT JOIN [EntityType] AS [et] ON [et].[Id] = [u].[EntityTypeId]
WHERE [u].[ForeignKey] = @ForeignKey
    AND ( [et].[Name] IS NULL OR [et].[Name] <> 'Rock.Security.Authentication.Database'
        OR [u].[Password] IS NULL OR LEFT( [u].[Password], 4 ) NOT IN ( '$2a$', '$2b$', '$2y$' ) OR LEN( [u].[Password] ) <> 60
        OR [u].[PersonId] IS NULL OR [u].[IsPasswordChangeRequired] = 1 OR [u].[IsLockedOut] = 1 OR [u].[IsConfirmed] IS NULL
        OR ( [u].[LastLoginDateTime] IS NOT NULL AND ( [u].[LastLoginDateTime] < [u].[CreatedDateTime] OR [u].[IsConfirmed] = 0 ) ) );

-- check: login-username-is-plain
-- why: Decision 179. Usernames follow Rock's rule (first initial and last name) in lowercase letters and digits only, and the sample data never takes Rock's own admin.
SELECT COUNT(*)
FROM [UserLogin] AS [u]
WHERE [u].[ForeignKey] = @ForeignKey
    AND ( [u].[UserName] LIKE '%[^a-z0-9]%' COLLATE Latin1_General_BIN OR [u].[UserName] = 'admin' );

-- check: login-belongs-to-an-adult
-- why: Decision 179. Only adults 18 and older have logins, never a child or a business.
SELECT COUNT(*)
FROM [UserLogin] AS [u]
INNER JOIN [Person] AS [p] ON [p].[Id] = [u].[PersonId]
WHERE [u].[ForeignKey] = @ForeignKey
    AND ( [p].[AgeClassification] <> 1 OR [p].[Age] < 18 OR [p].[RecordTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' ) );

-- check: security-role-member-can-sign-in
-- why: Decision 79 and 179. Staff can sign in and see what their roles allow, so everyone the load puts in a security role has a login.
SELECT COUNT( DISTINCT [gm].[PersonId] )
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [gm].[ForeignKey] = @ForeignKey AND [g].[IsSecurityRole] = 1 AND [gm].[IsArchived] = 0
    AND NOT EXISTS ( SELECT 1 FROM [UserLogin] AS [u] WHERE [u].[PersonId] = [gm].[PersonId] );

-- check: app-user-can-sign-in
-- why: Decision 179. A church app device tied to a person means they signed in to the app.
SELECT COUNT( DISTINCT [pa].[PersonId] )
FROM [PersonalDevice] AS [pd]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [pd].[PersonAliasId]
WHERE [pd].[ForeignKey] = @ForeignKey
    AND NOT EXISTS ( SELECT 1 FROM [UserLogin] AS [u] WHERE [u].[PersonId] = [pa].[PersonId] );

-- check: login-has-its-history
-- why: Decision 181. Rock's login save hook records each new login on the person's History tab, so every sample login has its "User Login" row.
SELECT COUNT(*)
FROM [UserLogin] AS [u]
WHERE [u].[ForeignKey] = @ForeignKey
    AND NOT EXISTS ( SELECT 1 FROM [History] AS [h]
        WHERE [h].[EntityId] = [u].[PersonId] AND [h].[RelatedEntityId] = [u].[Id] AND [h].[ValueName] = 'User Login' AND [h].[NewValue] = [u].[UserName] AND [h].[ForeignKey] = @ForeignKey );

-- check: account-protection-matches-rock
-- why: Decision 182. Rock's Process Elevated Security job sets each person's account protection profile: Extreme in a security role marked Extreme, High in one marked High or with an active scheduled gift or saved account, Medium with a login, and Low otherwise.
SELECT COUNT(*)
FROM [Person] AS [p]
WHERE [p].[ForeignKey] = @ForeignKey
    AND [p].[AccountProtectionProfile] <> CASE
        WHEN EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
            WHERE [gm].[PersonId] = [p].[Id] AND [gm].[IsArchived] = 0 AND [g].[IsActive] = 1 AND ( [g].[IsSecurityRole] = 1 OR [gt].[Guid] = 'AECE949F-704C-483E-A4FB-93D5E4720C4C' ) AND [g].[ElevatedSecurityLevel] = 2 ) THEN 3
        WHEN EXISTS ( SELECT 1 FROM [GroupMember] AS [gm] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
                WHERE [gm].[PersonId] = [p].[Id] AND [gm].[IsArchived] = 0 AND [g].[IsActive] = 1 AND ( [g].[IsSecurityRole] = 1 OR [gt].[Guid] = 'AECE949F-704C-483E-A4FB-93D5E4720C4C' ) AND [g].[ElevatedSecurityLevel] = 1 )
            OR EXISTS ( SELECT 1 FROM [FinancialScheduledTransaction] AS [s] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[AuthorizedPersonAliasId] WHERE [pa].[PersonId] = [p].[Id] AND [s].[IsActive] = 1 )
            OR EXISTS ( SELECT 1 FROM [FinancialPersonSavedAccount] AS [sa] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [sa].[PersonAliasId] WHERE [pa].[PersonId] = [p].[Id] ) THEN 2
        WHEN EXISTS ( SELECT 1 FROM [UserLogin] AS [u] WHERE [u].[PersonId] = [p].[Id] ) THEN 1
        ELSE 0 END;

-- check: registration-linkage-points-at-a-listed-occurrence
-- why: Decision 189. Rock's event page shows a registration through its linkage to an occurrence. An occurrence of an inactive, unapproved, or uncalendared event never shows, so neither does its Register button.
SELECT COUNT(*)
FROM [EventItemOccurrenceGroupMap] AS [m]
LEFT JOIN [EventItemOccurrence] AS [o] ON [o].[Id] = [m].[EventItemOccurrenceId]
LEFT JOIN [EventItem] AS [e] ON [e].[Id] = [o].[EventItemId]
WHERE [m].[ForeignKey] = @ForeignKey
    AND ( [e].[Id] IS NULL OR [e].[IsActive] = 0 OR [e].[IsApproved] = 0
        OR NOT EXISTS ( SELECT 1 FROM [EventCalendarItem] AS [ci] WHERE [ci].[EventItemId] = [e].[Id] ) );

-- check: registration-linkage-campus-matches-occurrence
-- why: A linkage names the campus of the occurrence it links, as Rock's linkage screen sets it. A church-wide occurrence has no campus.
SELECT COUNT(*)
FROM [EventItemOccurrenceGroupMap] AS [m]
INNER JOIN [EventItemOccurrence] AS [o] ON [o].[Id] = [m].[EventItemOccurrenceId]
WHERE [m].[ForeignKey] = @ForeignKey
    AND ISNULL( [m].[CampusId], 0 ) <> ISNULL( [o].[CampusId], 0 );

-- check: registration-linkage-instance-and-template-are-active
-- why: Rock offers registration only for an active instance of an active template. Every linkage names an instance.
SELECT COUNT(*)
FROM [EventItemOccurrenceGroupMap] AS [m]
LEFT JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [m].[RegistrationInstanceId]
LEFT JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [i].[RegistrationTemplateId]
WHERE [m].[ForeignKey] = @ForeignKey
    AND ( [i].[Id] IS NULL OR [i].[IsActive] = 0 OR [t].[IsActive] = 0 );

-- check: registration-slug-matches-rocks-pattern
-- why: Rock's linkage screen accepts only lowercase letters and digits in words joined by single hyphens (^[a-z0-9]+(?:-[a-z0-9]+)*$). The event page finds a registration by its slug, and Rock checks the pattern only in its screens.
SELECT COUNT(*)
FROM [EventItemOccurrenceGroupMap] AS [m]
WHERE [m].[ForeignKey] = @ForeignKey
    AND ( ISNULL( [m].[UrlSlug], '' ) = ''
        OR [m].[UrlSlug] COLLATE Latin1_General_BIN LIKE '%[^a-z0-9-]%'
        OR [m].[UrlSlug] LIKE '-%' OR [m].[UrlSlug] LIKE '%-' OR [m].[UrlSlug] LIKE '%--%' );

-- check: registration-slug-is-unique
-- why: The event page opens the one linkage whose slug is in the address. Rock checks uniqueness only in its screens.
SELECT COUNT(*)
FROM [EventItemOccurrenceGroupMap] AS [m]
WHERE [m].[ForeignKey] = @ForeignKey
    AND ( SELECT COUNT(*) FROM [EventItemOccurrenceGroupMap] AS [other] WHERE [other].[UrlSlug] = [m].[UrlSlug] ) > 1;

-- check: registration-dates-are-in-order
-- why: An instance opens before it closes, and its event starts after registration closes and on or after the payment deadline. A deadline after the event would leave balances due after everyone went home.
SELECT COUNT(*)
FROM [RegistrationInstance] AS [i]
WHERE [i].[ForeignKey] = @ForeignKey
    AND ( [i].[StartDateTime] IS NULL OR [i].[EndDateTime] IS NULL OR [i].[StartDateTime] >= [i].[EndDateTime]
        OR NOT EXISTS (
            SELECT 1
            FROM [EventItemOccurrenceGroupMap] AS [m]
            INNER JOIN [EventItemOccurrence] AS [o] ON [o].[Id] = [m].[EventItemOccurrenceId]
            INNER JOIN [ScheduleDate] AS [sd] ON [sd].[ScheduleId] = [o].[ScheduleId]
            WHERE [m].[RegistrationInstanceId] = [i].[Id] AND [sd].[StartDateTime] > [i].[EndDateTime]
                AND CAST( [sd].[StartDateTime] AS DATE ) >= ISNULL( [i].[PaymentDeadlineDate], CAST( [i].[EndDateTime] AS DATE ) ) ) );

-- check: placement-group-matches-its-placement
-- why: Rock's placement screen lists an instance's groups for a placement by the RelatedEntity's qualifier, the template placement's Id, and places people only in groups of that placement's group type. A group of another type, or a placement of another template, would show in the wrong column or not at all.
SELECT COUNT(*)
FROM [RelatedEntity] AS [r]
INNER JOIN [EntityType] AS [se] ON [se].[Id] = [r].[SourceEntityTypeId]
INNER JOIN [EntityType] AS [te] ON [te].[Id] = [r].[TargetEntityTypeId]
LEFT JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[SourceEntityId]
LEFT JOIN [Group] AS [g] ON [g].[Id] = [r].[TargetEntityId]
LEFT JOIN [RegistrationTemplatePlacement] AS [p] ON CAST( [p].[Id] AS NVARCHAR( 200 ) ) = [r].[QualifierValue]
WHERE [r].[ForeignKey] = @ForeignKey AND [r].[PurposeKey] = 'PLACEMENT'
    AND ( [se].[Name] <> 'Rock.Model.RegistrationInstance' OR [te].[Name] <> 'Rock.Model.Group'
        OR [i].[Id] IS NULL OR [g].[Id] IS NULL OR [p].[Id] IS NULL
        OR [p].[RegistrationTemplateId] <> [i].[RegistrationTemplateId] OR [g].[GroupTypeId] <> [p].[GroupTypeId] );

-- check: linked-group-is-an-event-attendees-group
-- why: Rock adds registrants to the linked group with the template's group type and role, so the group must be of the template's type. Every sample linked group is an Event Attendees group, under the Guid Rock reserves for that type.
SELECT COUNT(*)
FROM [EventItemOccurrenceGroupMap] AS [m]
INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [m].[RegistrationInstanceId]
INNER JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [i].[RegistrationTemplateId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [m].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
WHERE [m].[ForeignKey] = @ForeignKey
    AND ( [g].[GroupTypeId] <> ISNULL( [t].[GroupTypeId], 0 ) OR [gt].[Guid] <> '3311132B-268D-44E9-811A-A56A0835E50A' );

-- check: registration-category-has-the-admin-role
-- why: Rock's v19 rollup gave the RSR - Event Registration Administration role Edit and Administrate on every top-level template category that existed then. Categories added later need the same, or that role cannot manage their templates.
SELECT COUNT(*)
FROM [Category] AS [c]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [c].[EntityTypeId]
WHERE [c].[ForeignKey] = @ForeignKey AND [et].[Name] = 'Rock.Model.RegistrationTemplate'
    AND ( SELECT COUNT( DISTINCT [a].[Action] )
        FROM [Auth] AS [a]
        INNER JOIN [Group] AS [r] ON [r].[Id] = [a].[GroupId]
        INNER JOIN [EntityType] AS [ae] ON [ae].[Id] = [a].[EntityTypeId]
        WHERE [ae].[Name] = 'Rock.Model.Category' AND [a].[EntityId] = [c].[Id] AND [a].[AllowOrDeny] = 'A' AND [a].[Action] IN ( 'Edit', 'Administrate' )
            AND [r].[Guid] = '2A92086B-DFF0-4B9C-46CB-4DAD805615AF' ) <> 2;

-- check: seven-registrations-are-open-today
-- why: Decision 190. Camp, the mission trip, the fall retreat, Parents' Night Out, Money Matters, Date Night, and the Women's Retreat are open on every install day, so every registration feature can be tried. Rock's calendar item page shows Register for an active instance of an active template, inside its dates, linked to the occurrence (EventItemOccurrenceLava).
SELECT CASE WHEN (
        SELECT COUNT( DISTINCT [o].[EventItemId] )
        FROM [EventItemOccurrenceGroupMap] AS [m]
        INNER JOIN [EventItemOccurrence] AS [o] ON [o].[Id] = [m].[EventItemOccurrenceId]
        INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [m].[RegistrationInstanceId]
        INNER JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [i].[RegistrationTemplateId]
        WHERE [m].[ForeignKey] = @ForeignKey AND [i].[IsActive] = 1 AND [t].[IsActive] = 1
            AND [i].[StartDateTime] <= GETDATE() AND [i].[EndDateTime] >= GETDATE() ) >= 7 THEN 0 ELSE 1 END;

-- check: registrant-template-matches
-- why: Rock reads a registrant's forms, fees, and attributes from its template. A registrant, its registration, and the registration's instance all name one template.
SELECT COUNT(*)
FROM [RegistrationRegistrant] AS [rr]
INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId]
INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[RegistrationInstanceId]
WHERE [rr].[ForeignKey] = @ForeignKey
    AND ( [rr].[RegistrationTemplateId] <> [r].[RegistrationTemplateId] OR [r].[RegistrationTemplateId] <> [i].[RegistrationTemplateId] );

-- check: registrant-required-answers-are-filled
-- why: Rock's registration form does not save until every required question it shows is answered. A registrant has an answer to each required registrant question on its template, except one on the wait list that the wait list form does not show.
SELECT COUNT(*)
FROM [RegistrationRegistrant] AS [rr]
INNER JOIN [RegistrationTemplateForm] AS [f] ON [f].[RegistrationTemplateId] = [rr].[RegistrationTemplateId]
INNER JOIN [RegistrationTemplateFormField] AS [ff] ON [ff].[RegistrationTemplateFormId] = [f].[Id] AND [ff].[FieldSource] = 4 AND [ff].[IsRequired] = 1
LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [ff].[AttributeId] AND [av].[EntityId] = [rr].[Id]
WHERE [rr].[ForeignKey] = @ForeignKey
    AND ( [rr].[OnWaitList] = 0 OR [ff].[ShowOnWaitlist] = 1 )
    AND ISNULL( [av].[Value], '' ) = '';

-- check: registrant-is-in-the-grades
-- why: Rock checks a registrant's grade against the template's on the day they register (RegistrantEligibilityEvaluator), and the event is for those grades, so every registrant is in them on both days. A grade moves up each June 1, as check-in moves it.
WITH [Registrants] AS (
    SELECT [p].[GraduationYear], CAST( [r].[CreatedDateTime] AS DATE ) AS [Registered]
        , ( SELECT CAST( MIN( [sd].[StartDateTime] ) AS DATE )
            FROM [EventItemOccurrenceGroupMap] AS [m]
            INNER JOIN [EventItemOccurrence] AS [o] ON [o].[Id] = [m].[EventItemOccurrenceId]
            INNER JOIN [ScheduleDate] AS [sd] ON [sd].[ScheduleId] = [o].[ScheduleId]
            WHERE [m].[RegistrationInstanceId] = [i].[Id] AND [sd].[StartDateTime] > [i].[EndDateTime] ) AS [EventDay]
        , 12 - CAST( JSON_VALUE( [t].[AdditionalSettingsJson], '$.RegistrantEligibilitySettings.MaximumGradeOffset' ) AS INT ) AS [MinGrade]
        , 12 - CAST( JSON_VALUE( [t].[AdditionalSettingsJson], '$.RegistrantEligibilitySettings.MinimumGradeOffset' ) AS INT ) AS [MaxGrade]
    FROM [RegistrationRegistrant] AS [rr]
    INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId]
    INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[RegistrationInstanceId]
    INNER JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [i].[RegistrationTemplateId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [rr].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    WHERE [rr].[ForeignKey] = @ForeignKey AND JSON_VALUE( [t].[AdditionalSettingsJson], '$.RegistrantEligibilitySettings.MaximumGradeOffset' ) IS NOT NULL
), [Grades] AS (
    SELECT [MinGrade], [MaxGrade], [EventDay]
        , 12 - ( [GraduationYear] - ( YEAR( [Registered] ) + CASE WHEN [Registered] >= DATEFROMPARTS( YEAR( [Registered] ), 6, 1 ) THEN 1 ELSE 0 END ) ) AS [GradeRegistered]
        , 12 - ( [GraduationYear] - ( YEAR( [EventDay] ) + CASE WHEN [EventDay] >= DATEFROMPARTS( YEAR( [EventDay] ), 6, 1 ) THEN 1 ELSE 0 END ) ) AS [GradeAtEvent]
    FROM [Registrants]
)
SELECT COUNT(*)
FROM [Grades]
WHERE [EventDay] IS NULL OR [GradeRegistered] IS NULL
    OR [GradeRegistered] NOT BETWEEN [MinGrade] AND [MaxGrade] OR [GradeAtEvent] NOT BETWEEN [MinGrade] AND [MaxGrade];

-- check: registration-is-inside-its-window
-- why: A registration is made while its instance is open, and before the install day. One dated outside its window could never have been made in Rock.
SELECT COUNT(*)
FROM [Registration] AS [r]
INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[RegistrationInstanceId]
WHERE [r].[ForeignKey] = @ForeignKey
    AND ( [r].[CreatedDateTime] < [i].[StartDateTime] OR [r].[CreatedDateTime] > [i].[EndDateTime] OR [r].[CreatedDateTime] >= CAST( GETDATE() AS DATE )
        OR [r].[CreatedDateKey] <> CAST( CONVERT( CHAR( 8 ), [r].[CreatedDateTime], 112 ) AS INT ) );

-- check: registrants-fit-the-limit-and-join-the-group
-- why: Rock lets no more registrants than MaxAttendees off the wait list, and adds each of them to the linkage's group with the template's role and status (AddRegistrantsToGroup), pointing the registrant at that member. Wait-listed registrants are in no group.
SELECT ( SELECT COUNT(*)
        FROM [RegistrationInstance] AS [i]
        WHERE [i].[ForeignKey] = @ForeignKey AND [i].[MaxAttendees] IS NOT NULL
            AND ( SELECT COUNT(*) FROM [RegistrationRegistrant] AS [rr] INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId]
                WHERE [r].[RegistrationInstanceId] = [i].[Id] AND [rr].[OnWaitList] = 0 ) > [i].[MaxAttendees] )
    + ( SELECT COUNT(*)
        FROM [RegistrationRegistrant] AS [rr]
        INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId]
        INNER JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [r].[RegistrationTemplateId]
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [rr].[PersonAliasId]
        LEFT JOIN [GroupMember] AS [gm] ON [gm].[Id] = [rr].[GroupMemberId]
        WHERE [rr].[ForeignKey] = @ForeignKey
            AND ( ( [rr].[OnWaitList] = 1 AND [rr].[GroupMemberId] IS NOT NULL )
                OR ( [rr].[OnWaitList] = 0 AND [r].[GroupId] IS NOT NULL
                    AND ( [gm].[Id] IS NULL OR [gm].[GroupId] <> [r].[GroupId] OR [gm].[PersonId] <> [pa].[PersonId]
                        OR [gm].[GroupRoleId] <> [t].[GroupMemberRoleId] OR [gm].[GroupMemberStatus] <> [t].[GroupMemberStatus] ) ) ) );

-- check: wait-list-only-when-full
-- why: Rock puts a registrant on the wait list only when the instance has no spots left. A wait list beside an open spot would never be moved up.
SELECT COUNT(*)
FROM [RegistrationInstance] AS [i]
WHERE [i].[ForeignKey] = @ForeignKey
    AND EXISTS ( SELECT 1 FROM [RegistrationRegistrant] AS [rr] INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId] WHERE [r].[RegistrationInstanceId] = [i].[Id] AND [rr].[OnWaitList] = 1 )
    AND ( [i].[MaxAttendees] IS NULL
        OR ( SELECT COUNT(*) FROM [RegistrationRegistrant] AS [rr] INNER JOIN [Registration] AS [r] ON [r].[Id] = [rr].[RegistrationId]
            WHERE [r].[RegistrationInstanceId] = [i].[Id] AND [rr].[OnWaitList] = 0 ) < [i].[MaxAttendees]
        OR NOT EXISTS ( SELECT 1 FROM [RegistrationTemplate] AS [t] WHERE [t].[Id] = [i].[RegistrationTemplateId] AND [t].[WaitListEnabled] = 1 ) );

-- check: placed-person-is-a-registrant
-- why: Rock's placement board lists an instance's registrants and the groups linked to it for each placement. A placed person who is not a registrant, or is on the wait list, of an instance linked to the group would show in a group with no registration behind them.
SELECT COUNT(*)
FROM [GroupMember] AS [gm]
WHERE [gm].[ForeignKey] = @ForeignKey
    AND EXISTS ( SELECT 1 FROM [RelatedEntity] AS [re] WHERE [re].[PurposeKey] = 'PLACEMENT' AND [re].[TargetEntityId] = [gm].[GroupId] AND [re].[ForeignKey] = @ForeignKey )
    AND NOT EXISTS (
        SELECT 1
        FROM [RelatedEntity] AS [re]
        INNER JOIN [Registration] AS [r] ON [r].[RegistrationInstanceId] = [re].[SourceEntityId]
        INNER JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[Id] AND [rr].[OnWaitList] = 0
        INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [rr].[PersonAliasId]
        WHERE [re].[PurposeKey] = 'PLACEMENT' AND [re].[TargetEntityId] = [gm].[GroupId] AND [pa].[PersonId] = [gm].[PersonId] );

-- check: registration-payment-is-rocks
-- why: Rock's registration entry saves a payment as an Event Registration transaction whose detail points at the registration and posts to its instance's account. A payment of another type, account, or registration would be missing from the balance, or counted as a gift.
SELECT COUNT(*)
FROM [FinancialTransactionDetail] AS [d]
INNER JOIN [FinancialTransaction] AS [ft] ON [ft].[Id] = [d].[TransactionId]
LEFT JOIN [EntityType] AS [et] ON [et].[Id] = [d].[EntityTypeId]
LEFT JOIN [Registration] AS [r] ON [r].[Id] = [d].[EntityId] AND [et].[Name] = 'Rock.Model.Registration'
LEFT JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[RegistrationInstanceId]
WHERE [d].[ForeignKey] = @ForeignKey
    AND ( [ft].[TransactionTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '33CB96DD-8752-4BEE-A142-88DB7DE538F0' ) OR [et].[Name] = 'Rock.Model.Registration' )
    AND ( [r].[Id] IS NULL OR [i].[AccountId] <> [d].[AccountId]
        OR [ft].[TransactionTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '33CB96DD-8752-4BEE-A142-88DB7DE538F0' )
        OR [ft].[AuthorizedPersonAliasId] <> [r].[PersonAliasId] );

-- check: registration-is-never-overpaid
-- why: Rock's balance is the discounted cost (Registration.DiscountedCost) less the payments. More paid than owed is a refund nobody made. Once the payment deadline has passed, every balance is paid.
WITH [Registrants] AS (
    SELECT [r].[Id], [i].[PaymentDeadlineDate]
        , CASE WHEN [rr].[OnWaitList] = 1 THEN 0 ELSE
            [rr].[Cost] - CASE WHEN [rr].[DiscountApplies] = 1 THEN [rr].[Cost] * [r].[DiscountPercentage] ELSE 0 END
            + ISNULL( [fees].[Other], 0 ) + ISNULL( [fees].[Discountable], 0 ) * CASE WHEN [rr].[DiscountApplies] = 1 THEN 1 - [r].[DiscountPercentage] ELSE 1 END
            - CASE WHEN [rr].[DiscountApplies] = 1 THEN [r].[DiscountAmount] ELSE 0 END END AS [Cost]
    FROM [Registration] AS [r]
    INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[RegistrationInstanceId]
    INNER JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[Id]
    OUTER APPLY (
        SELECT SUM( CASE WHEN [tf].[DiscountApplies] = 1 THEN [f].[Quantity] * [f].[Cost] ELSE 0 END ) AS [Discountable]
            , SUM( CASE WHEN [tf].[DiscountApplies] = 0 THEN [f].[Quantity] * [f].[Cost] ELSE 0 END ) AS [Other]
        FROM [RegistrationRegistrantFee] AS [f]
        INNER JOIN [RegistrationTemplateFee] AS [tf] ON [tf].[Id] = [f].[RegistrationTemplateFeeId]
        WHERE [f].[RegistrationRegistrantId] = [rr].[Id] ) AS [fees]
    WHERE [r].[ForeignKey] = @ForeignKey
), [Costs] AS (
    SELECT [Id], [PaymentDeadlineDate], SUM( CASE WHEN [Cost] > 0 THEN [Cost] ELSE 0 END ) AS [Cost]
    FROM [Registrants]
    GROUP BY [Id], [PaymentDeadlineDate]
), [Paid] AS (
    SELECT [c].[Id], [c].[Cost], [c].[PaymentDeadlineDate]
        , ISNULL( ( SELECT SUM( [d].[Amount] ) FROM [FinancialTransactionDetail] AS [d] INNER JOIN [EntityType] AS [et] ON [et].[Id] = [d].[EntityTypeId]
            WHERE [et].[Name] = 'Rock.Model.Registration' AND [d].[EntityId] = [c].[Id] ), 0 ) AS [Paid]
    FROM [Costs] AS [c]
)
SELECT COUNT(*)
FROM [Paid]
WHERE [Paid] > [Cost] OR ( [PaymentDeadlineDate] < CAST( GETDATE() AS DATE ) AND [Paid] < [Cost] );

-- check: payment-plan-fits-its-template
-- why: Rock offers a payment plan only at the template's frequencies, as an Event Registration scheduled transaction from the registrar, with one detail that points at the registration, and its last payment on or before the payment deadline. Each payment it made is one of its transactions.
WITH [Plans] AS (
    SELECT [s].*, [r].[Id] AS [RegistrationId], [r].[PersonAliasId] AS [RegistrarAliasId], [t].[PaymentPlanFrequencyValueIds], [i].[PaymentDeadlineDate], [f].[Guid] AS [FrequencyGuid]
        , [s].[NumberOfPayments] - 1 AS [LastIndex]
    FROM [Registration] AS [r]
    INNER JOIN [FinancialScheduledTransaction] AS [s] ON [s].[Id] = [r].[PaymentPlanFinancialScheduledTransactionId]
    INNER JOIN [RegistrationInstance] AS [i] ON [i].[Id] = [r].[RegistrationInstanceId]
    INNER JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [i].[RegistrationTemplateId]
    INNER JOIN [DefinedValue] AS [f] ON [f].[Id] = [s].[TransactionFrequencyValueId]
    WHERE [r].[ForeignKey] = @ForeignKey
)
SELECT COUNT(*)
FROM [Plans] AS [p]
WHERE [p].[NumberOfPayments] IS NULL OR [p].[NumberOfPayments] < 2 OR [p].[PaymentDeadlineDate] IS NULL
    OR CONCAT( ',', [p].[PaymentPlanFrequencyValueIds], ',' ) NOT LIKE CONCAT( '%,', [p].[TransactionFrequencyValueId], ',%' )
    OR [p].[TransactionTypeValueId] <> ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '33CB96DD-8752-4BEE-A142-88DB7DE538F0' )
    OR [p].[AuthorizedPersonAliasId] <> [p].[RegistrarAliasId]
    OR ( SELECT COUNT(*) FROM [FinancialScheduledTransactionDetail] AS [d] INNER JOIN [EntityType] AS [et] ON [et].[Id] = [d].[EntityTypeId]
        WHERE [d].[ScheduledTransactionId] = [p].[Id] AND [et].[Name] = 'Rock.Model.Registration' AND [d].[EntityId] = [p].[RegistrationId] ) <> 1
    OR ( SELECT COUNT(*) FROM [FinancialTransaction] AS [ft] WHERE [ft].[ScheduledTransactionId] = [p].[Id] ) > [p].[NumberOfPayments]
    OR CASE
        WHEN [p].[FrequencyGuid] = '1400753C-A0F9-4A45-8A1D-81C98450BD1F' THEN DATEADD( MONTH, [p].[LastIndex], [p].[StartDate] )
        WHEN DAY( [p].[StartDate] ) = 1 THEN DATEADD( DAY, CASE WHEN [p].[LastIndex] % 2 = 1 THEN 14 ELSE 0 END, DATEADD( MONTH, [p].[LastIndex] / 2, [p].[StartDate] ) )
        ELSE DATEADD( DAY, CASE WHEN [p].[LastIndex] % 2 = 1 THEN -14 ELSE 0 END, DATEADD( MONTH, ( [p].[LastIndex] + 1 ) / 2, [p].[StartDate] ) )
        END > [p].[PaymentDeadlineDate];

-- check: discount-code-within-its-uses
-- why: Rock stops taking a code once as many registrations of an instance have used it as its MaxUsage allows (GetDiscountByCodeIfValid).
SELECT COUNT(*)
FROM [RegistrationTemplateDiscount] AS [d]
INNER JOIN [RegistrationInstance] AS [i] ON [i].[RegistrationTemplateId] = [d].[RegistrationTemplateId]
WHERE [d].[ForeignKey] = @ForeignKey AND [d].[MaxUsage] IS NOT NULL
    AND ( SELECT COUNT(*) FROM [Registration] AS [r] WHERE [r].[RegistrationInstanceId] = [i].[Id] AND [r].[DiscountCode] = [d].[Code] ) > [d].[MaxUsage];

-- check: last-camp-matches-camp-steps
-- why: Jon's decision. When the steps part loaded, the people registered for a Student Camp that has ended are exactly the people with a Student Camp step at that camp, both ways, so the registration and the step tell one story.
WITH [CampDays] AS (
    SELECT DISTINCT [i].[Id] AS [InstanceId], CAST( [sd].[StartDateTime] AS DATE ) AS [CampDay]
    FROM [RegistrationInstance] AS [i]
    INNER JOIN [RegistrationTemplate] AS [t] ON [t].[Id] = [i].[RegistrationTemplateId]
    INNER JOIN [EventItemOccurrenceGroupMap] AS [m] ON [m].[RegistrationInstanceId] = [i].[Id]
    INNER JOIN [EventItemOccurrence] AS [o] ON [o].[Id] = [m].[EventItemOccurrenceId]
    INNER JOIN [ScheduleDate] AS [sd] ON [sd].[ScheduleId] = [o].[ScheduleId]
    WHERE [i].[ForeignKey] = @ForeignKey AND [t].[Guid] = 'ABF10164-8D9E-4FF0-B4FC-173F35015923'
        AND [sd].[StartDateTime] > [i].[EndDateTime] AND [sd].[EndDateTime] < CAST( GETDATE() AS DATE )
), [Campers] AS (
    SELECT DISTINCT [pa].[PersonId], [c].[CampDay]
    FROM [CampDays] AS [c]
    INNER JOIN [Registration] AS [r] ON [r].[RegistrationInstanceId] = [c].[InstanceId]
    INNER JOIN [RegistrationRegistrant] AS [rr] ON [rr].[RegistrationId] = [r].[Id] AND [rr].[OnWaitList] = 0
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [rr].[PersonAliasId]
), [CampSteps] AS (
    SELECT DISTINCT [pa].[PersonId], CAST( [s].[StartDateTime] AS DATE ) AS [CampDay]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
    WHERE [s].[ForeignKey] = @ForeignKey AND [st].[Guid] = 'EED45E21-7602-47A7-A204-6658F2E712A3'
        AND CAST( [s].[StartDateTime] AS DATE ) IN ( SELECT [CampDay] FROM [CampDays] )
)
SELECT CASE WHEN NOT EXISTS ( SELECT 1 FROM [Step] WHERE [ForeignKey] = @ForeignKey ) THEN 0
    ELSE ( SELECT COUNT(*) FROM ( SELECT * FROM [Campers] EXCEPT SELECT * FROM [CampSteps] ) AS [x] )
        + ( SELECT COUNT(*) FROM ( SELECT * FROM [CampSteps] EXCEPT SELECT * FROM [Campers] ) AS [x] ) END;
