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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;
using static RockSampleData.Loader.Loading.RockStreaks;

namespace RockSampleData.Loader.Loading
{
    /*
        10/19/26 - CLAUDE

        Two "todays" are in play. Rock processes achievements when a streak is saved, and a live check-in saves the
        child's streak the first time they check in each week. Successful attempts are never deleted, and the rest
        are rebuilt on each run, so a child's attempts end up exactly as one run on their last check-in Sunday would
        leave them. After that nothing processes them again, so an attempt still open then stays open.

        The streak row's counts are different: Rock's RockCleanup job refreshes them every day (without processing
        achievements), so they are as of the anchor.

        Reason: Streaks and attempts must match what Rock would show after years of live check-in.
    */

    /// <summary>
    /// Creates the kids check-in streak type and its achievement types from configuration/achievements.json, and writes
    /// each child's streak and achievement attempts as Rock would have computed them from their check-ins.
    /// </summary>
    /// <remarks>
    /// The math is ported in <see cref="RockStreaks"/>. The input is the attendance plan held in memory, because
    /// Attendance's indexes are not rebuilt until after small groups.
    /// </remarks>
    internal sealed class AchievementsLoader
    {
        private static readonly Guid StreakEntityTypeGuid = new Guid( "D953B0A5-0065-4624-8844-10010DE01E5C" );
        private static readonly Guid PersonAliasEntityTypeGuid = new Guid( "90F5E87B-F0D5-4617-8AE9-EB57E673F36F" );
        private static readonly Guid AchievementTypeEntityTypeGuid = new Guid( "0E99356C-0DEA-4F24-944E-21CD5FA83B9E" );
        private static readonly Guid ConsecutiveComponentGuid = new Guid( "174F0AFF-3A5E-4A20-AE8B-D8D83D43BACD" );
        private static readonly Guid AccumulativeComponentGuid = new Guid( "05D8CD17-E07D-4927-B9C4-5018F7C4B715" );

        /// <summary>
        /// Rock's shipped trophy and medal images, the same ones its own Weekly Attendance achievements use.
        /// </summary>
        private static readonly Guid TrophyBinaryFileGuid = new Guid( "9A1503BC-D965-4BD4-AEA4-8039D4657201" );
        private static readonly Guid MedalBinaryFileGuid = new Guid( "80331F03-4F4B-46B3-B789-8D34C12B4F42" );

        /// <summary>
        /// The check-in template setting (core_checkin_AchievementTypes) that lists the achievement types next-gen
        /// check-in shows on its success screen and labels.
        /// </summary>
        private static readonly Guid CheckInAchievementTypesAttributeGuid = new Guid( "EECDA094-E5E2-4A47-804D-65701590F2A1" );

        private const int CheckInConfigStructureType = 4;

        private readonly RockDatabase _db;
        private readonly AchievementConfiguration _configuration;
        private readonly SundayCalendar _calendar;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;

        public AchievementsLoader( RockDatabase db, AchievementConfiguration configuration, SundayCalendar calendar, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _configuration = configuration;
            _calendar = calendar;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Writes the streak type, the achievement types, and every child's streak and attempts.
        /// <paramref name="checkInSundaysByAlias"/> holds each child's check-in Sundays by primary alias, and
        /// <paramref name="checkInSundays"/> the Sundays with any kids check-in.
        /// </summary>
        public void Load( Dictionary<int, SortedSet<DateTime>> checkInSundaysByAlias, SortedSet<DateTime> checkInSundays )
        {
            var now = DateTime.Now;
            var definitions = ValidatedDefinitions();

            // Rock's rebuild starts the occurrence map from a fresh 128-byte array and sets a bit for each week
            // with a check-in.
            var streakType = new StreakType
            {
                StartDate = _calendar.Sundays[0],
                FirstDayOfWeek = null,
                Frequency = OccurrenceFrequency.Weekly,
                OccurrenceMap = AllocateNewByteArray()
            };

            foreach ( var sunday in checkInSundays )
            {
                streakType.OccurrenceMap = SetBit( streakType, streakType.OccurrenceMap, sunday, true );
            }

            var streakTypeId = CreateStreakType( streakType, checkInSundays.Count > 0 ? checkInSundays.Max : streakType.StartDate );
            var typeIds = CreateAchievementTypes( definitions );

            var streaks = TableFactory.Create( "StreakTypeId:int", "PersonAliasId:int", "EnrollmentDate:date", "EngagementMap:bytes", "CurrentStreakStartDate:date", "CurrentStreakCount:int",
                "LongestStreakStartDate:date", "LongestStreakEndDate:date", "LongestStreakCount:int", "EngagementCount:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var attempts = TableFactory.Create( "AchieverEntityId:int", "AchievementTypeId:int", "Progress:decimal", "IsClosed:bool", "IsSuccessful:bool",
                "AchievementAttemptStartDateTime:date", "AchievementAttemptEndDateTime:date", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var attemptCounts = definitions.ToDictionary( d => d.Key, _ => 0 );

            foreach ( var (aliasId, sundays) in checkInSundaysByAlias.OrderBy( p => p.Key ) )
            {
                // Rock's rebuild makes the engagement map the same length as the occurrence map.
                var engagementMap = new byte[streakType.OccurrenceMap.Length];
                foreach ( var sunday in sundays )
                {
                    engagementMap = SetBit( streakType, engagementMap, sunday, true );
                }

                var enrollmentDate = AlignDate( sundays.Min, streakType );
                var lastCheckIn = AlignDate( sundays.Max, streakType );
                var streak = new Streak( enrollmentDate, engagementMap );
                var data = GetStreakData( streakType, streak, _dates.Anchor );

                streaks.Rows.Add( streakTypeId, aliasId, enrollmentDate, engagementMap, Nullable( data.CurrentStreakStartDate ), data.CurrentStreakCount,
                    Nullable( data.LongestStreakStartDate ), Nullable( data.LongestStreakEndDate ), data.LongestStreakCount, data.EngagementCount,
                    Guid.NewGuid(), enrollmentDate, lastCheckIn, _foreignKey );

                // Prerequisites first, as Rock sorts them. A type with an unmet prerequisite is not processed at all.
                var results = new Dictionary<string, List<Attempt>>();
                foreach ( var definition in definitions )
                {
                    if ( definition.Prerequisite != null && !results[definition.Prerequisite].Any( a => a.IsSuccessful ) )
                    {
                        results[definition.Key] = new List<Attempt>();
                        continue;
                    }

                    var rules = new AchievementRules( definition.IsConsecutive, definition.Target, definition.MaxAccomplishmentsAllowed );
                    results[definition.Key] = Process( rules, streakType, streak, new List<Attempt>(), lastCheckIn );

                    foreach ( var attempt in results[definition.Key] )
                    {
                        attempts.Rows.Add( aliasId, typeIds[definition.Key], ProgressColumn( attempt.Progress ), attempt.IsClosed, attempt.IsSuccessful,
                            attempt.StartDateTime, Nullable( attempt.EndDateTime ), Guid.NewGuid(), attempt.StartDateTime, attempt.EndDateTime ?? attempt.StartDateTime, _foreignKey );
                        attemptCounts[definition.Key]++;
                    }
                }
            }

            _db.BulkInsert( "Streak", streaks, keepIdentity: false );
            _db.BulkInsert( "AchievementAttempt", attempts, keepIdentity: false );
            ShowOnCheckIn( definitions, now );

            var counts = definitions.Select( d => $"{d.Name} {attemptCounts[d.Key]:N0}" );
            Console.WriteLine( $"  Streaks: {streaks.Rows.Count:N0} kids over {checkInSundays.Count} weeks, attempts: {attempts.Rows.Count:N0} ({string.Join( ", ", counts )})" );
        }

        /// <summary>
        /// The achievement types in processing order, after checking that each prerequisite comes first.
        /// </summary>
        private List<AchievementTypeDefinition> ValidatedDefinitions()
        {
            var seen = new HashSet<string>();
            foreach ( var definition in _configuration.AchievementTypes )
            {
                if ( definition.Component != "consecutive" && definition.Component != "accumulative" )
                {
                    throw new InvalidOperationException( $"configuration/achievements.json: {definition.Key} has component \"{definition.Component}\"; use consecutive or accumulative." );
                }

                if ( definition.Target < 1 )
                {
                    throw new InvalidOperationException( $"configuration/achievements.json: {definition.Key} needs a target of at least 1." );
                }

                if ( definition.Prerequisite != null && !seen.Contains( definition.Prerequisite ) )
                {
                    throw new InvalidOperationException( $"configuration/achievements.json: {definition.Key} needs {definition.Prerequisite}, which must come before it." );
                }

                seen.Add( definition.Key );
            }

            return _configuration.AchievementTypes;
        }

        /// <summary>
        /// Writes the streak type, created when the history starts and last changed by the newest check-in week.
        /// </summary>
        private int CreateStreakType( StreakType streakType, DateTime lastChanged )
        {
            var definition = _configuration.StreakType;
            var templateId = _db.RequireIdByGuid( "GroupType", definition.CheckInTemplateGuid );

            /*
                10/19/26 - CLAUDE

                EnableAttendance is on. Rock's attendance save hook marks engagement only on streak types with
                EnableAttendance set (StreakTypeService.HandleAttendanceRecordForStreak), and Rock's Streak Type
                Detail block turns it on for every new streak type. With it off, check-ins after install would
                never reach these streaks or their achievements.

                Reason: Kids keep earning achievements at check-in after the sample data is installed.
            */
            return ( int ) _db.Scalar( @"
INSERT INTO [StreakType] ( [Name], [Description], [StructureType], [StructureEntityId], [EnableAttendance], [RequiresEnrollment], [OccurrenceFrequency], [StartDate],
    [OccurrenceMap], [IsActive], [FirstDayOfWeek], [StructureSettingsJSON], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @StructureType, @StructureEntityId, 1, 0, @OccurrenceFrequency, @StartDate,
    @OccurrenceMap, 1, NULL, '{""IncludeChildAccounts"":false}', @Guid, @CreatedDateTime, @ModifiedDateTime, @ForeignKey )",
                P( "@Name", definition.Name ), P( "@Description", definition.Description ), P( "@StructureType", CheckInConfigStructureType ), P( "@StructureEntityId", templateId ),
                P( "@OccurrenceFrequency", ( int ) streakType.Frequency ), P( "@StartDate", streakType.StartDate ), P( "@OccurrenceMap", streakType.OccurrenceMap ),
                P( "@Guid", definition.Guid ), P( "@CreatedDateTime", streakType.StartDate ), P( "@ModifiedDateTime", lastChanged ), P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Writes the achievement types, their component settings, and their prerequisites. Returns each type's Id by key.
        /// </summary>
        private Dictionary<string, int> CreateAchievementTypes( List<AchievementTypeDefinition> definitions )
        {
            var streakEntityTypeId = _db.RequireIdByGuid( "EntityType", StreakEntityTypeGuid );
            var personAliasEntityTypeId = _db.RequireIdByGuid( "EntityType", PersonAliasEntityTypeGuid );
            var achievementTypeEntityTypeId = _db.RequireIdByGuid( "EntityType", AchievementTypeEntityTypeGuid );
            var consecutiveId = _db.RequireIdByGuid( "EntityType", ConsecutiveComponentGuid );
            var accumulativeId = _db.RequireIdByGuid( "EntityType", AccumulativeComponentGuid );
            var trophyId = _db.IdByGuid( "BinaryFile", TrophyBinaryFileGuid );
            var medalId = _db.IdByGuid( "BinaryFile", MedalBinaryFileGuid );
            var created = _calendar.Sundays[0];
            var streakTypeGuid = _configuration.StreakType.Guid.ToString().ToUpperInvariant();

            // Component settings are attribute values on the achievement type, keyed by component.
            int ComponentAttributeId( int componentId, string key ) => Convert.ToInt32( _db.Scalar( @"
SELECT [Id] FROM [Attribute]
WHERE [EntityTypeId] = @EntityTypeId AND [EntityTypeQualifierColumn] = 'ComponentEntityTypeId' AND [EntityTypeQualifierValue] = @ComponentId AND [Key] = @Key",
                P( "@EntityTypeId", achievementTypeEntityTypeId ), P( "@ComponentId", componentId.ToString() ), P( "@Key", key ) )
                ?? throw new InvalidOperationException( $"Rock has no {key} setting for achievement component {componentId}." ) );

            var ids = new Dictionary<string, int>();
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            foreach ( var definition in definitions )
            {
                var componentId = definition.IsConsecutive ? consecutiveId : accumulativeId;
                var id = ( int ) _db.Scalar( @"
INSERT INTO [AchievementType] ( [Name], [Description], [ComponentEntityTypeId], [ComponentConfigJson], [SourceEntityTypeId], [AchieverEntityTypeId], [AchievementIconCssClass],
    [MaxAccomplishmentsAllowed], [AllowOverAchievement], [IsActive], [IsPublic], [ImageBinaryFileId], [HighlightColor], [TargetCount], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @ComponentEntityTypeId, @ComponentConfigJson, @SourceEntityTypeId, @AchieverEntityTypeId, @AchievementIconCssClass,
    @MaxAccomplishmentsAllowed, 0, 1, 1, @ImageBinaryFileId, @HighlightColor, @TargetCount, @Guid, @CreatedDateTime, @CreatedDateTime, @ForeignKey )",
                    P( "@Name", definition.Name ), P( "@Description", definition.Description ), P( "@ComponentEntityTypeId", componentId ),
                    P( "@ComponentConfigJson", $"{{\"StreakType\":\"{streakTypeGuid}\"}}" ), P( "@SourceEntityTypeId", streakEntityTypeId ), P( "@AchieverEntityTypeId", personAliasEntityTypeId ),
                    P( "@AchievementIconCssClass", definition.IconCssClass ), P( "@MaxAccomplishmentsAllowed", definition.MaxAccomplishmentsAllowed ),
                    P( "@ImageBinaryFileId", definition.IsConsecutive ? trophyId : medalId ), P( "@HighlightColor", definition.HighlightColor ), P( "@TargetCount", definition.Target ),
                    P( "@Guid", definition.Guid ), P( "@CreatedDateTime", created ), P( "@ForeignKey", _foreignKey ) );
                ids[definition.Key] = id;

                var targetKey = definition.IsConsecutive ? "NumberToAchieve" : "NumberToAccumulate";
                values.Rows.Add( false, ComponentAttributeId( componentId, "StreakType" ), id, streakTypeGuid, Guid.NewGuid(), created, created, _foreignKey );
                values.Rows.Add( false, ComponentAttributeId( componentId, targetKey ), id, definition.Target.ToString(), Guid.NewGuid(), created, created, _foreignKey );

                if ( definition.Prerequisite != null )
                {
                    _db.Execute( @"
INSERT INTO [AchievementTypePrerequisite] ( [AchievementTypeId], [PrerequisiteAchievementTypeId], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @AchievementTypeId, @PrerequisiteAchievementTypeId, NEWID(), @CreatedDateTime, @CreatedDateTime, @ForeignKey )",
                        P( "@AchievementTypeId", id ), P( "@PrerequisiteAchievementTypeId", ids[definition.Prerequisite] ), P( "@CreatedDateTime", created ), P( "@ForeignKey", _foreignKey ) );
                }
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            return ids;
        }

        /// <summary>
        /// Lists the achievement types in the check-in template's Achievement Types setting, comma delimited, so next-gen
        /// check-in shows each child's progress and badges.
        /// </summary>
        private void ShowOnCheckIn( List<AchievementTypeDefinition> definitions, DateTime now )
        {
            var attributeId = _db.RequireIdByGuid( "Attribute", CheckInAchievementTypesAttributeGuid );
            var templateId = _db.RequireIdByGuid( "GroupType", _configuration.StreakType.CheckInTemplateGuid );
            var value = string.Join( ",", definitions.Select( d => d.Guid.ToString() ) );

            _db.Execute( @"
UPDATE [AttributeValue] SET [Value] = @Value, [ModifiedDateTime] = @Now WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId;
IF @@ROWCOUNT = 0
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, NEWID(), @Now, @Now, @ForeignKey );",
                P( "@Value", value ), P( "@AttributeId", attributeId ), P( "@EntityId", templateId ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Rock stores Progress in a decimal(18,9) column, and Rock's data layer cuts extra digits off (it does not round).
        /// </summary>
        private static decimal ProgressColumn( decimal progress )
        {
            return decimal.Truncate( progress * 1_000_000_000m ) / 1_000_000_000m;
        }

        private static object Nullable( DateTime? value )
        {
            return value.HasValue ? value.Value : DBNull.Value;
        }
    }
}
