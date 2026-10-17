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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockSampleData.Loader.Model
{
    /*
        10/16/26 - CLAUDE

        One plan shape for every small group. The Cast's plans live in cast/groups/*.json
        (written by --plan-cast-groups); generated households get plans from the planner at
        load time. The loader only ever sees plans. Meeting style, lifecycle, and membership
        are unions, so an online group cannot have a host and a member who left always has
        a date they left.

        Reason: One typed model keeps the Cast files, the planner, and the loader in agreement.
    */

    /// <summary>
    /// Everything the loader needs to create one small group, its members, and its history.
    /// </summary>
    internal sealed class SmallGroupPlan
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// A campus key, or "main" for the main campus (as Cast households use).
        /// </summary>
        public string Campus { get; set; }

        /// <summary>
        /// A topic value from the configuration.
        /// </summary>
        public string Topic { get; set; }

        public string Description { get; set; }

        public GroupSchedule Schedule { get; set; }

        public MeetingStyle MeetingStyle { get; set; }

        public bool ChildCareProvided { get; set; }

        public int Capacity { get; set; }

        /// <summary>
        /// False hides the group from Group Finder (the Closed test case).
        /// </summary>
        public bool IsPublic { get; set; }

        public bool MeetsInSummer { get; set; }

        public AttendanceHabit AttendanceHabit { get; set; }

        public GroupLifecycle Lifecycle { get; set; }

        public SmallGroupTestCase TestCase { get; set; }

        public List<PlannedMember> Members { get; set; } = new List<PlannedMember>();
    }

    /// <summary>
    /// A weekly meeting day and start time.
    /// </summary>
    internal sealed record GroupSchedule( DayOfWeek Day, string Time );

    /// <summary>
    /// Where a group meets.
    /// </summary>
    [JsonPolymorphic( TypeDiscriminatorPropertyName = "style" )]
    [JsonDerivedType( typeof( InPersonMeeting ), "InPerson" )]
    [JsonDerivedType( typeof( OnlineMeeting ), "Online" )]
    internal abstract record MeetingStyle;

    /// <summary>
    /// Meets at the host household's home.
    /// </summary>
    internal sealed record InPersonMeeting( string HostHousehold ) : MeetingStyle;

    /// <summary>
    /// Meets online at a meeting link.
    /// </summary>
    internal sealed record OnlineMeeting( string Link ) : MeetingStyle;

    /// <summary>
    /// Whether the leader records attendance every week or only some weeks.
    /// </summary>
    internal enum AttendanceHabit
    {
        EveryWeek,
        Spotty
    }

    /// <summary>
    /// A group made for testing, or None for an ordinary group.
    /// </summary>
    internal enum SmallGroupTestCase
    {
        None,
        Full,
        Closed,
        Pending,
        Inactive,
        Archived
    }

    /// <summary>
    /// A group's life: still meeting, ended (inactive), or ended and later archived.
    /// </summary>
    [JsonPolymorphic( TypeDiscriminatorPropertyName = "state" )]
    [JsonDerivedType( typeof( ActiveLifecycle ), "Active" )]
    [JsonDerivedType( typeof( InactiveLifecycle ), "Inactive" )]
    [JsonDerivedType( typeof( ArchivedLifecycle ), "Archived" )]
    internal abstract record GroupLifecycle( [property: JsonPropertyOrder( 1 )] RelativeDate Started );

    internal sealed record ActiveLifecycle( RelativeDate Started ) : GroupLifecycle( Started );

    /// <summary>
    /// Ended on a date, for a reason (an inactive reason value from the configuration).
    /// </summary>
    internal sealed record InactiveLifecycle( RelativeDate Started, [property: JsonPropertyOrder( 2 )] RelativeDate Ended, [property: JsonPropertyOrder( 3 )] string Reason ) : GroupLifecycle( Started );

    /// <summary>
    /// Ended on a date, then archived on a later date.
    /// </summary>
    internal sealed record ArchivedLifecycle( RelativeDate Started, [property: JsonPropertyOrder( 2 )] RelativeDate Ended, [property: JsonPropertyOrder( 3 )] RelativeDate Archived ) : GroupLifecycle( Started );

    /// <summary>
    /// A member's role in a small group.
    /// </summary>
    internal enum SmallGroupRole
    {
        Leader,
        Host,
        Member
    }

    /// <summary>
    /// One person's membership: still a member, left (inactive or archived), or a pending request.
    /// Joined is the date added, or the date of the request for a pending member.
    /// </summary>
    [JsonPolymorphic( TypeDiscriminatorPropertyName = "state" )]
    [JsonDerivedType( typeof( ActiveMember ), "Active" )]
    [JsonDerivedType( typeof( InactiveMember ), "Inactive" )]
    [JsonDerivedType( typeof( ArchivedMember ), "Archived" )]
    [JsonDerivedType( typeof( PendingMember ), "Pending" )]
    internal abstract record PlannedMember(
        [property: JsonPropertyOrder( 1 )] Guid Guid,
        [property: JsonPropertyOrder( 2 )] string Name,
        [property: JsonPropertyOrder( 3 )] SmallGroupRole Role,
        [property: JsonPropertyOrder( 4 )] RelativeDate Joined,
        [property: JsonPropertyOrder( 6 )] int AttendancePercent );

    internal sealed record ActiveMember( Guid Guid, string Name, SmallGroupRole Role, RelativeDate Joined, int AttendancePercent )
        : PlannedMember( Guid, Name, Role, Joined, AttendancePercent );

    internal sealed record InactiveMember( Guid Guid, string Name, SmallGroupRole Role, RelativeDate Joined, [property: JsonPropertyOrder( 5 )] RelativeDate Left, int AttendancePercent )
        : PlannedMember( Guid, Name, Role, Joined, AttendancePercent );

    internal sealed record ArchivedMember( Guid Guid, string Name, SmallGroupRole Role, RelativeDate Joined, [property: JsonPropertyOrder( 5 )] RelativeDate Left, int AttendancePercent )
        : PlannedMember( Guid, Name, Role, Joined, AttendancePercent );

    internal sealed record PendingMember( Guid Guid, string Name, SmallGroupRole Role, RelativeDate Joined, int AttendancePercent )
        : PlannedMember( Guid, Name, Role, Joined, AttendancePercent );

    /// <summary>
    /// Reads and writes plans in the Cast group file format.
    /// </summary>
    internal static class SmallGroupPlanJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowOutOfOrderMetadataProperties = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter(), new UppercaseGuidConverter() }
        };
    }

    /// <summary>
    /// Writes Guids in uppercase, as every Cast file does.
    /// </summary>
    internal sealed class UppercaseGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options ) => reader.GetGuid();

        public override void Write( Utf8JsonWriter writer, Guid value, JsonSerializerOptions options ) => writer.WriteStringValue( value.ToString().ToUpperInvariant() );
    }

    /// <summary>
    /// Reads either relative date form and writes only the form in use, as the household schema requires.
    /// </summary>
    internal sealed class RelativeDateConverter : JsonConverter<RelativeDate>
    {
        public override RelativeDate Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
        {
            if ( reader.TokenType != JsonTokenType.StartObject )
            {
                throw new JsonException( "A relative date must be an object." );
            }

            var date = new RelativeDate();
            while ( reader.Read() && reader.TokenType != JsonTokenType.EndObject )
            {
                var name = reader.GetString();
                reader.Read();
                switch ( name?.ToLowerInvariant() )
                {
                    case "daysago":
                        date.DaysAgo = reader.GetInt32();
                        break;
                    case "yearsago":
                        date.YearsAgo = reader.GetInt32();
                        break;
                    case "month":
                        date.Month = reader.GetInt32();
                        break;
                    case "day":
                        date.Day = reader.GetInt32();
                        break;
                    case "time":
                        date.Time = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return date;
        }

        public override void Write( Utf8JsonWriter writer, RelativeDate value, JsonSerializerOptions options )
        {
            writer.WriteStartObject();
            if ( value.DaysAgo.HasValue )
            {
                writer.WriteNumber( "daysAgo", value.DaysAgo.Value );
            }
            else
            {
                writer.WriteNumber( "yearsAgo", value.YearsAgo );
                writer.WriteNumber( "month", value.Month );
                writer.WriteNumber( "day", value.Day );
            }

            if ( !string.IsNullOrEmpty( value.Time ) )
            {
                writer.WriteString( "time", value.Time );
            }

            writer.WriteEndObject();
        }
    }
}
