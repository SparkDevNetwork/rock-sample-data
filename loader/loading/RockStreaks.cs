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

namespace RockSampleData.Loader.Loading
{
    /*
        10/19/26 - CLAUDE

        A line-for-line port of Rock's streak and streak-based achievement math, so the loader can write the
        streaks and achievement attempts Rock would have computed. Keep it a faithful copy: same bit layout,
        same week alignment, same order of operations, same decimal progress. Do not "improve" it.

        Rock sources this mirrors (paths in the SparkDevNetwork/Rock repository):
        - Rock/Model/Engagement/StreakType/StreakTypeService.cs (SetBit, AlignDate, IncrementDateTime,
          GetFrequencyUnitDifference, GetMaxDateForStreakBreaking, AndBitOperation, PadLeft, AllocateNewByteArray,
          GetByteFromMap, IterateMaps, IterateStreakMap, GetStreakData)
        - Rock.Common/RockDateTime.cs (GetSundayDate and the Monday default first day of the week)
        - Rock/Achievement/StreakSourcedAchievementComponent.cs (Process)
        - Rock/Achievement/AchievementComponent.cs (CalculateMinDateForAchievementAttempt,
          CalculateMaxDateForAchievementAttempt, CalculateProgress, CalculateDeficiency)
        - Rock/Achievement/Component/StreakAchievement.cs (Streaks: Consecutive, CreateNewAttempts)
        - Rock/Achievement/Component/AccumulativeAchievement.cs (Streaks: Accumulative, CreateNewAttempts)

        Rock reads RockDateTime.Now and Today inside these methods. The port takes "now" as a parameter, so the
        loader can run each one as of the day Rock would have run it.

        The sample church's achievement types have no timespan, no start or end date, no exclusions, and never
        allow over-achievement, so the branches for those are left out. Everything else follows Rock exactly.

        The version guard (validation/rock-lock.json) fails when any of these change in Rock.

        Reason: Streaks and achievements on a fresh install must match what Rock itself computes.
    */

    /// <summary>
    /// The port of Rock's streak and streak-based achievement math. Nothing here touches a database.
    /// </summary>
    internal static class RockStreaks
    {
        private const int DaysPerWeek = 7;

        private const int BitsPerByte = 8;

        private const int MapByteGrowthCount = 128;

        /// <summary>
        /// Rock's RockDateTime.DefaultFirstDayOfWeek, used when a streak type leaves FirstDayOfWeek empty and the
        /// church has not changed the system setting.
        /// </summary>
        private const DayOfWeek DefaultFirstDayOfWeek = DayOfWeek.Monday;

        /// <summary>
        /// Rock's StreakOccurrenceFrequency.
        /// </summary>
        public enum OccurrenceFrequency
        {
            Daily = 0,
            Weekly = 1,
            Monthly = 2,
            Yearly = 3
        }

        /// <summary>
        /// The parts of Rock's StreakTypeCache the math reads.
        /// </summary>
        public sealed class StreakType
        {
            public DateTime StartDate { get; init; }

            public DayOfWeek? FirstDayOfWeek { get; init; }

            public OccurrenceFrequency Frequency { get; init; }

            public byte[] OccurrenceMap { get; set; }
        }

        /// <summary>
        /// One person's streak row: when they enrolled, and the weeks they engaged.
        /// </summary>
        public sealed record Streak( DateTime EnrollmentDate, byte[] EngagementMap );

        /// <summary>
        /// The denormalized values Rock copies from GetStreakData onto the Streak row (StreakService.CopyStreakDataToStreakModel).
        /// </summary>
        public sealed record StreakData( int EngagementCount, int CurrentStreakCount, DateTime? CurrentStreakStartDate, int LongestStreakCount, DateTime? LongestStreakStartDate, DateTime? LongestStreakEndDate );

        /// <summary>
        /// The settings of one streak-based achievement type that the math reads.
        /// </summary>
        public sealed record AchievementRules( bool IsConsecutive, int Target, int? MaxAccomplishmentsAllowed );

        /// <summary>
        /// One achievement attempt, with the columns Rock's components set (AchievementAttempt).
        /// </summary>
        public sealed class Attempt
        {
            public DateTime StartDateTime { get; set; }

            public DateTime? EndDateTime { get; set; }

            public decimal Progress { get; set; }

            public bool IsClosed { get; set; }

            public bool IsSuccessful { get; set; }
        }

        /// <summary>
        /// Rock's ComputedStreak.
        /// </summary>
        private sealed class ComputedStreak
        {
            public ComputedStreak( DateTime startDate )
            {
                StartDate = startDate;
            }

            public DateTime StartDate { get; }

            public DateTime? EndDate { get; set; }

            public int Count { get; set; }
        }

        #region Maps

        /// <summary>
        /// Port of StreakTypeService.SetBit.
        /// </summary>
        public static byte[] SetBit( StreakType streakType, byte[] map, DateTime bitDate, bool newValue )
        {
            var mapStartDate = AlignDate( streakType.StartDate, streakType );
            bitDate = AlignDate( bitDate, streakType );

            if ( bitDate < mapStartDate )
            {
                throw new InvalidOperationException( "The specified date occurs before the streak type start date" );
            }

            var unitsFromStart = GetFrequencyUnitDifference( mapStartDate, bitDate, streakType, false );
            var bytesNeeded = ( unitsFromStart / BitsPerByte ) + 1;

            if ( map == null )
            {
                map = AllocateNewByteArray( bytesNeeded );
            }
            else if ( bytesNeeded > map.Length )
            {
                // Grow the map to accommodate the new value
                map = PadLeft( map, bytesNeeded );
            }

            // Set the target bit within it's byte
            var byteIndex = map.Length - bytesNeeded;
            var byteBitValue = ( byte ) ( 1 << ( unitsFromStart % BitsPerByte ) );

            if ( newValue )
            {
                map[byteIndex] |= byteBitValue;
            }
            else
            {
                map[byteIndex] &= ( byte ) ~byteBitValue;
            }

            return map;
        }

        /// <summary>
        /// Port of StreakTypeService.AllocateNewByteArray. Maps grow in steps of 128 bytes.
        /// </summary>
        public static byte[] AllocateNewByteArray( int? bytesNeeded = null )
        {
            var growthUnits = ( ( bytesNeeded ?? 0 ) / MapByteGrowthCount ) + 1;
            return new byte[growthUnits * MapByteGrowthCount];
        }

        /// <summary>
        /// Port of StreakTypeService.PadLeft: adds 0's to the start or left side of the map.
        /// </summary>
        private static byte[] PadLeft( byte[] map, int newSize )
        {
            if ( newSize <= map.Length )
            {
                return map;
            }

            var newMap = AllocateNewByteArray( newSize );
            var offset = newMap.Length - map.Length;

            for ( var i = 0; i < map.Length; i++ )
            {
                newMap[i + offset] = map[i];
            }

            return newMap;
        }

        /// <summary>
        /// Port of StreakTypeService.AndBitOperation: ANDs the bits in-place on top of array a.
        /// </summary>
        private static void AndBitOperation( byte[] a, byte[] b )
        {
            // Start at the right side (least significant bit) of each array
            var aIndex = a.Length - 1;
            var bIndex = b.Length - 1;

            while ( aIndex >= 0 && bIndex >= 0 )
            {
                a[aIndex] &= b[bIndex];

                // Move left
                aIndex--;
                bIndex--;
            }
        }

        /// <summary>
        /// Port of StreakTypeService.GetByteFromMap. Returns zero if the index is outside the array.
        /// </summary>
        private static byte GetByteFromMap( byte[] map, int byteIndex )
        {
            if ( byteIndex < 0 || byteIndex >= map.Length )
            {
                return 0;
            }

            return map[byteIndex];
        }

        /// <summary>
        /// Port of StreakTypeService.IterateMaps. The action gets (unit, date, hasOccurrence, hasEngagement,
        /// hasExclusion) and returns true to stop early.
        /// </summary>
        private static void IterateMaps( StreakType streakType, DateTime iterationStartDate, DateTime iterationEndDate, byte[] engagementMap, byte[] exclusionMap, Func<int, DateTime, bool, bool, bool, bool> actionPerIteration, DateTime now )
        {
            var today = AlignDate( now, streakType );
            var maxDate = AlignDate( iterationEndDate, streakType );
            var minDate = AlignDate( iterationStartDate, streakType );
            var mapStartDate = AlignDate( streakType.StartDate, streakType );

            if ( maxDate > today )
            {
                throw new InvalidOperationException( "The max date cannot be in the future" );
            }

            if ( minDate < mapStartDate )
            {
                throw new InvalidOperationException( "The min date cannot be before the map start date" );
            }

            if ( minDate > maxDate )
            {
                throw new InvalidOperationException( "The max date must be after the min date" );
            }

            // Calculate the difference in min date to the map start date
            var slideStartUnitsToFuture = GetFrequencyUnitDifference( mapStartDate, minDate, streakType, false );

            // Calculate the number of frequency units that the results are based upon (inclusive)
            var numberOfFrequencyUnits = GetFrequencyUnitDifference( minDate, maxDate, streakType, true );

            // Prepare to iterate over the bytes
            var currentUnit = 0;
            var currentDate = minDate;
            var initialByteOffset = ( slideStartUnitsToFuture / BitsPerByte ) + 1;
            var currentByteBitValue = 1 << ( slideStartUnitsToFuture % BitsPerByte );

            var occurrenceMap = streakType.OccurrenceMap ?? new byte[0];
            var occurrenceMapLength = occurrenceMap.Length;
            var currentOccurrenceByteIndex = occurrenceMapLength - initialByteOffset;
            var currentOccurrenceByte = GetByteFromMap( occurrenceMap, currentOccurrenceByteIndex );

            engagementMap = engagementMap ?? new byte[0];
            var engagementMapLength = engagementMap.Length;
            var currentEngagementByteIndex = engagementMapLength - initialByteOffset;
            var currentEngagementByte = GetByteFromMap( engagementMap, currentEngagementByteIndex );

            exclusionMap = exclusionMap ?? new byte[0];
            var exclusionMapLength = exclusionMap.Length;
            var currentExclusionByteIndex = exclusionMapLength - initialByteOffset;
            var currentExclusionByte = GetByteFromMap( exclusionMap, currentExclusionByteIndex );

            // Loop for each unit expected in the date range
            while ( currentUnit < numberOfFrequencyUnits )
            {
                var hasEngagement = ( currentEngagementByte & currentByteBitValue ) == currentByteBitValue;
                var hasExclusion = ( currentExclusionByte & currentByteBitValue ) == currentByteBitValue;
                var hasOccurrence = ( currentOccurrenceByte & currentByteBitValue ) == currentByteBitValue;

                // Call the action with the iteration variables
                var isDone = actionPerIteration( currentUnit, currentDate, hasOccurrence, hasEngagement, hasExclusion );

                if ( isDone )
                {
                    break;
                }

                // Iterate to the next bit
                currentUnit++;
                currentByteBitValue = currentByteBitValue << 1;
                currentDate = IncrementDateTime( currentDate, streakType.Frequency );

                // If the bit value is beyond the current byte, then increment to the next byte
                if ( currentByteBitValue > byte.MaxValue )
                {
                    currentByteBitValue = 1;

                    currentOccurrenceByteIndex--;
                    currentOccurrenceByte = GetByteFromMap( occurrenceMap, currentOccurrenceByteIndex );

                    currentEngagementByteIndex--;
                    currentEngagementByte = GetByteFromMap( engagementMap, currentEngagementByteIndex );

                    currentExclusionByteIndex--;
                    currentExclusionByte = GetByteFromMap( exclusionMap, currentExclusionByteIndex );
                }
            }
        }

        /// <summary>
        /// Port of StreakTypeService.IterateStreakMap for a person with one streak row and no exclusions.
        /// </summary>
        private static void IterateStreakMap( StreakType streakType, Streak streak, DateTime startDate, DateTime endDate, Func<int, DateTime, bool, bool, bool, bool> iterationAction, DateTime now )
        {
            // Rock ORs the person's engagement maps into a fresh array (one map is used as is, read from the database
            // without tracking). The copy keeps the AND below from changing the loader's own row.
            var aggregateEngagementMap = ( byte[] ) streak.EngagementMap.Clone();

            // Make sure there are no engagements where occurrences do not exist
            AndBitOperation( aggregateEngagementMap, streakType.OccurrenceMap ?? new byte[aggregateEngagementMap.Length] );

            // Validate and adjust the dates
            var streakTypeMapStartDate = AlignDate( streakType.StartDate, streakType );

            // Apply default values to parameters
            if ( startDate < streakTypeMapStartDate )
            {
                startDate = streakTypeMapStartDate;
            }

            if ( endDate > now.Date )
            {
                endDate = now.Date;
            }

            startDate = AlignDate( startDate, streakType );
            endDate = AlignDate( endDate, streakType );

            if ( startDate > endDate )
            {
                // There is nothing to be iterated over
                return;
            }

            IterateMaps( streakType, startDate, endDate, aggregateEngagementMap, null, iterationAction, now );
        }

        /// <summary>
        /// Port of StreakTypeService.GetStreakData with the default start and end dates: the values Rock writes to the
        /// streak row each time it refreshes it (on save, and in the RockCleanup job every day).
        /// </summary>
        public static StreakData GetStreakData( StreakType streakType, Streak streak, DateTime now )
        {
            var maxDate = AlignDate( now, streakType );
            var streakTypeMapStartDate = AlignDate( streakType.StartDate, streakType );

            // Apply default values to parameters
            DateTime startDate = streakTypeMapStartDate;
            DateTime endDate = now.Date;

            if ( endDate < startDate )
            {
                endDate = startDate;
            }

            // Adjust the start and stop dates based on the selected frequency
            startDate = AlignDate( startDate, streakType );
            endDate = AlignDate( endDate, streakType );

            // Validate the parameters
            if ( startDate > maxDate )
            {
                throw new InvalidOperationException( "StartDate cannot be in the future" );
            }

            if ( endDate > maxDate )
            {
                throw new InvalidOperationException( "EndDate cannot be in the future" );
            }

            // Calculate the aggregate engagement map, which are all of the engagement maps ORed together
            var aggregateEngagementMap = ( byte[] ) streak.EngagementMap.Clone();

            // Make sure there are no engagements where occurrences do not exist
            AndBitOperation( aggregateEngagementMap, streakType.OccurrenceMap ?? new byte[aggregateEngagementMap.Length] );

            ComputedStreak currentStreak = null;
            ComputedStreak longestStreak = null;
            DateTime? lastOccurrenceDate = null;
            var engagementCount = 0;

            // Get the max date that streaks can be broken. This is to avoid breaking streaks while people still have time to
            // engage in that day or week (because it is the current day or week)
            var maxDateForStreakBreaking = GetMaxDateForStreakBreaking( streakType, now );

            // Iterate over the maps from the start to the end date
            bool iterationAction( int currentUnit, DateTime currentDate, bool hasOccurrence, bool hasEngagement, bool hasExclusion )
            {
                if ( hasOccurrence )
                {
                    if ( hasEngagement )
                    {
                        engagementCount++;

                        // If starting a new streak, record the date
                        if ( currentStreak == null )
                        {
                            currentStreak = new ComputedStreak( currentDate );
                        }

                        // Count this engagement toward the current streak
                        currentStreak.Count++;

                        // If this is now the longest streak, update the longest counters
                        if ( longestStreak == null || currentStreak.Count > longestStreak.Count )
                        {
                            longestStreak = new ComputedStreak( currentStreak.StartDate )
                            {
                                EndDate = currentDate,
                                Count = currentStreak.Count
                            };
                        }
                    }
                    else if ( hasExclusion )
                    {
                        // Excluded/excused absences don't count toward streaks in a positive nor a negative manner
                    }
                    else if ( currentDate <= maxDateForStreakBreaking )
                    {
                        // Break the current streak
                        if ( currentStreak != null )
                        {
                            currentStreak.EndDate = lastOccurrenceDate;
                            currentStreak = null;
                        }
                    }

                    lastOccurrenceDate = currentDate;
                }

                return false;
            }

            IterateMaps( streakType, startDate, endDate, aggregateEngagementMap, null, iterationAction, now );

            return new StreakData(
                engagementCount,
                currentStreak?.Count ?? 0,
                currentStreak?.StartDate,
                longestStreak?.Count ?? 0,
                longestStreak?.StartDate,
                longestStreak?.EndDate );
        }

        #endregion Maps

        #region Dates

        /// <summary>
        /// Port of StreakTypeService.AlignDate.
        /// </summary>
        public static DateTime AlignDate( DateTime dateTime, StreakType streakType )
        {
            switch ( streakType.Frequency )
            {
                case OccurrenceFrequency.Daily:
                case OccurrenceFrequency.Monthly:
                case OccurrenceFrequency.Yearly:
                    return dateTime.Date;
                case OccurrenceFrequency.Weekly:
                    return GetSundayDate( dateTime, streakType.FirstDayOfWeek ?? DefaultFirstDayOfWeek );
                default:
                    throw new NotImplementedException( $"Getting aligned date for the StreakOccurrenceFrequency '{streakType.Frequency}' is not implemented" );
            }
        }

        /// <summary>
        /// Port of RockDateTime.GetSundayDate: the Sunday that labels the week the date falls in.
        /// </summary>
        public static DateTime GetSundayDate( DateTime inputDate, DayOfWeek firstDayOfWeek )
        {
            // Get the number of days until the next Sunday date
            int sundayDiff = 7 - ( int ) inputDate.DayOfWeek;

            // Figure out which DayOfWeek would be the lastDayOfWeek ( which would be the DayOfWeek before the firstDayOfWeek )
            DayOfWeek lastDayOfWeek;

            if ( firstDayOfWeek == DayOfWeek.Sunday )
            {
                // The day before Sunday is Saturday
                lastDayOfWeek = DayOfWeek.Saturday;
            }
            else
            {
                // if the startOfWeek isn't Sunday, we can just subtract by 1
                lastDayOfWeek = firstDayOfWeek - 1;
            }

            DateTime sundayDate;

            if ( inputDate.DayOfWeek == DayOfWeek.Sunday )
            {
                sundayDate = inputDate;
            }
            else if ( lastDayOfWeek < inputDate.DayOfWeek )
            {
                // If the lastDayOfWeek after the current day of week, we can simply add
                sundayDate = inputDate.AddDays( sundayDiff );
            }
            else
            {
                // If the current DayOfWeek is on or after the lastDayOfWeek, it'll be the *previous* Sunday.
                sundayDate = inputDate.AddDays( sundayDiff - 7 );
            }

            return sundayDate.Date;
        }

        /// <summary>
        /// Port of StreakTypeService.IncrementDateTime (forward only).
        /// </summary>
        private static DateTime IncrementDateTime( DateTime dateTime, OccurrenceFrequency frequency )
        {
            switch ( frequency )
            {
                case OccurrenceFrequency.Daily:
                    return dateTime.AddDays( 1 );
                case OccurrenceFrequency.Weekly:
                    return dateTime.AddDays( DaysPerWeek );
                case OccurrenceFrequency.Monthly:
                    return dateTime.AddMonths( 1 );
                case OccurrenceFrequency.Yearly:
                    return dateTime.AddYears( 1 );
                default:
                    throw new NotImplementedException( $"Increment date/time for the StreakOccurrenceFrequency '{frequency}' is not implemented" );
            }
        }

        /// <summary>
        /// Port of StreakTypeService.GetFrequencyUnitDifference: the number of frequency units between two dates.
        /// </summary>
        private static int GetFrequencyUnitDifference( DateTime startDate, DateTime endDate, StreakType streakType, bool isInclusive )
        {
            startDate = AlignDate( startDate, streakType );
            endDate = AlignDate( endDate, streakType );

            // Calculate the difference in days
            var timeSpan = endDate.Subtract( startDate );
            var numberOfDays = timeSpan.Days;
            var numberOfMonths = 0;
            var numberOfYears = 0;

            switch ( streakType.Frequency )
            {
                case OccurrenceFrequency.Daily:
                    // Adjust to be inclusive if needed
                    if ( isInclusive && numberOfDays >= 0 )
                    {
                        numberOfDays += 1;
                    }
                    else if ( isInclusive )
                    {
                        numberOfDays -= 1;
                    }

                    return numberOfDays;
                case OccurrenceFrequency.Weekly:
                    if ( isInclusive && numberOfDays >= 0 )
                    {
                        numberOfDays += DaysPerWeek;
                    }
                    else if ( isInclusive )
                    {
                        numberOfDays -= DaysPerWeek;
                    }

                    // Convert from days to the frequency units
                    return numberOfDays / DaysPerWeek;
                case OccurrenceFrequency.Monthly:
                    numberOfMonths = ( ( endDate.Year - startDate.Year ) * 12 ) + endDate.Month - startDate.Month;
                    if ( isInclusive )
                    {
                        numberOfMonths += 1;
                    }

                    return numberOfMonths;
                case OccurrenceFrequency.Yearly:
                    numberOfYears = endDate.Year - startDate.Year;
                    if ( isInclusive )
                    {
                        numberOfYears += 1;
                    }

                    return numberOfYears;
                default:
                    throw new NotImplementedException( $"Get Frequency Unit Difference for the StreakOccurrenceFrequency '{streakType.Frequency}' is not implemented" );
            }
        }

        /// <summary>
        /// Port of StreakTypeService.GetMaxDateForStreakBreaking: the end of the last fully elapsed unit. A streak is not
        /// broken while people still have time to engage in the current day or week.
        /// </summary>
        private static DateTime GetMaxDateForStreakBreaking( StreakType streakType, DateTime now )
        {
            var currentBitDate = AlignDate( now, streakType );

            switch ( streakType.Frequency )
            {
                case OccurrenceFrequency.Daily:
                    return currentBitDate.AddDays( -1 );
                case OccurrenceFrequency.Weekly:
                    return currentBitDate.AddDays( -1 * DaysPerWeek );
                case OccurrenceFrequency.Monthly:
                    return currentBitDate.AddMonths( -1 );
                case OccurrenceFrequency.Yearly:
                    return currentBitDate.AddYears( -1 );
                default:
                    throw new NotImplementedException( $"Get Max Date For Streak Breaking for the StreakOccurrenceFrequency '{streakType.Frequency}' is not implemented" );
            }
        }

        #endregion Dates

        #region Achievements

        /// <summary>
        /// Port of StreakSourcedAchievementComponent.Process for one person's streak and one achievement type.
        /// <paramref name="attempts"/> are the person's attempts of this type so far; the return value is what Rock
        /// leaves in the table afterward. The caller checks prerequisites first, as Process does
        /// (AchievementTypeService.GetUnmetPrerequisites: each prerequisite needs at least one successful attempt).
        /// </summary>
        public static List<Attempt> Process( AchievementRules rules, StreakType streakType, Streak streak, List<Attempt> attempts, DateTime now )
        {
            // Get all of the attempts for this streak and achievement combo, ordered by start date DESC so that
            // the most recent attempts can be found with FirstOrDefault
            attempts = attempts.OrderByDescending( saa => saa.StartDateTime ).ToList();

            var mostRecentSuccess = attempts.FirstOrDefault( saa => saa.EndDateTime.HasValue && saa.IsSuccessful );
            var successfulAttemptCount = attempts.Count( saa => saa.IsSuccessful );
            var maxSuccessesAllowed = rules.MaxAccomplishmentsAllowed ?? int.MaxValue;

            // If the success count limit has been reached, then no more processing should be done
            if ( successfulAttemptCount >= maxSuccessesAllowed )
            {
                return attempts;
            }

            // Everything after the most recent success is on the table for deletion. Successes should not be
            // deleted. Try to reuse these attempts if they match for continuity, but if the start date is changed, they
            // get deleted.
            var attemptsToDelete = attempts;

            if ( mostRecentSuccess != null )
            {
                attemptsToDelete = attemptsToDelete
                    .Where( saa => saa.StartDateTime > mostRecentSuccess.StartDateTime )
                    .ToList();
            }

            var kept = attempts.Except( attemptsToDelete ).ToList();
            attemptsToDelete = attemptsToDelete.ToList();

            var newAttempts = rules.IsConsecutive
                ? CreateNewConsecutiveAttempts( rules, streakType, streak, mostRecentSuccess, now )
                : CreateNewAccumulativeAttempts( rules, streakType, streak, mostRecentSuccess, now );

            if ( newAttempts != null && newAttempts.Any() )
            {
                newAttempts = newAttempts.OrderBy( saa => saa.StartDateTime ).ToList();

                foreach ( var newAttempt in newAttempts )
                {
                    // Keep the old attempt if possible, otherwise add a new one
                    var existingAttempt = attemptsToDelete.FirstOrDefault( saa => saa.StartDateTime == newAttempt.StartDateTime );

                    if ( existingAttempt != null )
                    {
                        attemptsToDelete.Remove( existingAttempt );
                        CopyAttempt( newAttempt, existingAttempt );
                        kept.Add( existingAttempt );
                    }
                    else
                    {
                        kept.Add( newAttempt );
                    }

                    // If this attempt was successful then make re-check the max success limit
                    if ( newAttempt.IsSuccessful )
                    {
                        successfulAttemptCount++;

                        if ( successfulAttemptCount >= maxSuccessesAllowed )
                        {
                            break;
                        }
                    }
                }
            }

            // Whatever is left in attemptsToDelete is deleted.
            return kept.OrderBy( a => a.StartDateTime ).ToList();
        }

        /// <summary>
        /// Port of StreakAchievement.CreateNewAttempts (Streaks: Consecutive), with no timespan, no start or end date,
        /// and no over-achievement.
        /// </summary>
        private static List<Attempt> CreateNewConsecutiveAttempts( AchievementRules rules, StreakType streakType, Streak streak, Attempt mostRecentClosedAttempt, DateTime now )
        {
            var numberToAchieve = rules.Target;

            // Calculate the date range where new achievements can be validly found
            var minDate = CalculateMinDateForAchievementAttempt( streak.EnrollmentDate, mostRecentClosedAttempt, numberToAchieve );
            var maxDate = CalculateMaxDateForAchievementAttempt( now );

            // Get the max date that streaks can be broken. This is to avoid breaking streaks while people still have time to
            // engage in that day or week (because it is the current day or week)
            var maxDateForStreakBreaking = GetMaxDateForStreakBreaking( streakType, now );

            // Track the attempts in a list that will be returned
            var attempts = new List<Attempt>();
            var streaks = new List<ComputedStreak>();

            // Define what happens for each bit in the date range
            bool iterationAction( int currentUnit, DateTime currentDate, bool hasOccurrence, bool hasEngagement, bool hasExclusion )
            {
                // If there is no timespan then only one attempt needs to be tracked at a time
                if ( hasOccurrence && hasEngagement && !streaks.Any() )
                {
                    streaks.Add( new ComputedStreak( currentDate ) );
                }
                else if ( hasOccurrence && !hasEngagement && !hasExclusion && streaks.Any() )
                {
                    // Break the streaks and close an attempt if there is an unexcused absence
                    var longestStreak = streaks.First();
                    attempts.Add( GetAttemptFromStreak( longestStreak, numberToAchieve, currentDate <= maxDateForStreakBreaking ) );
                    streaks.Clear();
                    return false;
                }

                for ( var i = streaks.Count - 1; i >= 0; i-- )
                {
                    var computedStreak = streaks[i];

                    if ( hasOccurrence && hasEngagement )
                    {
                        // Increment the streak
                        computedStreak.Count++;
                        computedStreak.EndDate = currentDate;

                        // Check for a fulfilled attempt
                        if ( computedStreak.Count >= numberToAchieve )
                        {
                            streaks.Clear();
                            attempts.Add( GetAttemptFromStreak( computedStreak, numberToAchieve, true ) );
                            break;
                        }
                    }
                }

                return false;
            }

            // Iterate through the streak date for the date range specified
            IterateStreakMap( streakType, streak, minDate, maxDate, iterationAction, now );

            // The longest leftover streak is an open attempt
            if ( streaks.Any() )
            {
                var longestStreak = streaks.First();
                attempts.Add( GetAttemptFromStreak( longestStreak, numberToAchieve, false ) );
            }

            return attempts;
        }

        /// <summary>
        /// Port of AccumulativeAchievement.CreateNewAttempts (Streaks: Accumulative), with no timespan, no start or end
        /// date, and no over-achievement.
        /// </summary>
        private static List<Attempt> CreateNewAccumulativeAttempts( AchievementRules rules, StreakType streakType, Streak streak, Attempt mostRecentSuccess, DateTime now )
        {
            var numberToAccumulate = rules.Target;

            // Calculate the date range where new achievements can be validly found
            var minDate = CalculateMinDateForAchievementAttempt( streak.EnrollmentDate, mostRecentSuccess, numberToAccumulate );
            var maxDate = CalculateMaxDateForAchievementAttempt( now );

            // Track the attempts in a list that will be returned
            var attempts = new List<Attempt>();
            var accumulations = new List<ComputedStreak>();

            // Define what happens for each bit in the date range
            bool iterationAction( int currentUnit, DateTime currentDate, bool hasOccurrence, bool hasEngagement, bool hasExclusion )
            {
                // If there is no timespan then only one attempt needs to be tracked at a time
                if ( hasOccurrence && hasEngagement && !accumulations.Any() )
                {
                    accumulations.Add( new ComputedStreak( currentDate ) );
                }

                for ( var i = accumulations.Count - 1; i >= 0; i-- )
                {
                    var accumulation = accumulations[i];

                    if ( hasOccurrence && hasEngagement )
                    {
                        // Increment the accumulation
                        accumulation.Count++;
                        accumulation.EndDate = currentDate;

                        // Check for a fulfilled attempt
                        if ( accumulation.Count >= numberToAccumulate )
                        {
                            accumulations.Clear();
                            attempts.Add( GetAttemptFromStreak( accumulation, numberToAccumulate, true ) );
                            break;
                        }
                    }
                }

                return false;
            }

            // Iterate through the streak date for the date range specified
            IterateStreakMap( streakType, streak, minDate, maxDate, iterationAction, now );

            // The longest leftover accumulation is an open attempt
            if ( accumulations.Any() )
            {
                var longestStreak = accumulations.First();
                attempts.Add( GetAttemptFromStreak( longestStreak, numberToAccumulate, false ) );
            }

            return attempts;
        }

        /// <summary>
        /// Port of StreakAchievement.ApplyStreakToAttempt and AccumulativeAchievement.GetAttempt, which set the same
        /// values.
        /// </summary>
        private static Attempt GetAttemptFromStreak( ComputedStreak computedStreak, int targetCount, bool isClosed )
        {
            var progress = CalculateProgress( computedStreak.Count, targetCount );

            return new Attempt
            {
                StartDateTime = computedStreak.StartDate,
                EndDateTime = computedStreak.EndDate,
                Progress = progress,
                IsClosed = isClosed,
                IsSuccessful = progress >= 1m
            };
        }

        /// <summary>
        /// Port of AchievementComponent.CopyAttempt.
        /// </summary>
        private static void CopyAttempt( Attempt source, Attempt target )
        {
            target.Progress = source.Progress;
            target.IsClosed = source.IsClosed;
            target.IsSuccessful = source.IsSuccessful;
            target.StartDateTime = source.StartDateTime;
            target.EndDateTime = source.EndDateTime;
        }

        /// <summary>
        /// Port of AchievementComponent.CalculateMinDateForAchievementAttempt, with no achievement type start date.
        /// </summary>
        private static DateTime CalculateMinDateForAchievementAttempt( DateTime minDate, Attempt mostRecentClosedAttempt, int targetCount )
        {
            if ( mostRecentClosedAttempt != null )
            {
                var deficiency = CalculateDeficiency( mostRecentClosedAttempt, targetCount );

                // If the most recent closed attempt has an end date, then the next attempt must be at least one day after
                if ( mostRecentClosedAttempt.EndDateTime.HasValue && deficiency == 0 )
                {
                    // We know the end date and it used all the bits, so just start after the end date.
                    minDate = mostRecentClosedAttempt.EndDateTime.Value.AddDays( 1 );
                }
                else if ( deficiency >= 1 )
                {
                    // Increment from the start date by the deficiency
                    minDate = mostRecentClosedAttempt.StartDateTime.AddDays( deficiency );
                }
                else
                {
                    // This shouldn't happen
                    minDate = mostRecentClosedAttempt.StartDateTime.AddDays( 1 );
                }
            }

            return minDate;
        }

        /// <summary>
        /// Port of AchievementComponent.CalculateMaxDateForAchievementAttempt, with no achievement type end date.
        /// </summary>
        private static DateTime CalculateMaxDateForAchievementAttempt( DateTime now )
        {
            // Use today as a starting point for the end date
            return now.Date;
        }

        /// <summary>
        /// Port of AchievementComponent.CalculateProgress.
        /// </summary>
        private static decimal CalculateProgress( int actualCount, int targetCount )
        {
            return decimal.Divide( actualCount, targetCount );
        }

        /// <summary>
        /// Port of AchievementComponent.CalculateDeficiency.
        /// </summary>
        private static int CalculateDeficiency( Attempt attempt, int targetCount )
        {
            var progress = attempt?.Progress ?? 0m;

            if ( progress < 0m )
            {
                progress = 0m;
            }
            else if ( progress > 1m )
            {
                progress = 1m;
            }

            var attemptCount = ( int ) decimal.Round( progress * targetCount );
            return targetCount - attemptCount;
        }

        #endregion Achievements
    }
}
