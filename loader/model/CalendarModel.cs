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

using System.Text.Json;

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// The contents of calendar.json: the church's calendars, audiences, contacts, and events (docs/calendar.md).
    /// </summary>
    internal sealed class CalendarConfiguration
    {
        /// <summary>
        /// Rock's calendars by key ("public", "internal").
        /// </summary>
        public Dictionary<string, Guid> Calendars { get; set; } = new Dictionary<string, Guid>();

        /// <summary>
        /// Rock's Audience Type values by key.
        /// </summary>
        public Dictionary<string, Guid> Audiences { get; set; } = new Dictionary<string, Guid>();

        /// <summary>
        /// How many months after the install day occurrences reach.
        /// </summary>
        public int MonthsAhead { get; set; }

        public Dictionary<string, CalendarContact> Contacts { get; set; } = new Dictionary<string, CalendarContact>();

        /// <summary>
        /// The contact key that stands in for the campus pastor on the main campus.
        /// </summary>
        public string MainCampusPastor { get; set; }

        public string CampusPastorPhone { get; set; }

        /// <summary>
        /// Room names a campus calls by its own name, by campus key and then by the room name events use.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> LocationNames { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        public List<CalendarEvent> Events { get; set; } = new List<CalendarEvent>();
    }

    /// <summary>
    /// A Cast person to contact, with the email and phone shown on the event.
    /// </summary>
    internal sealed class CalendarContact
    {
        public Guid Person { get; set; }

        public string Email { get; set; }

        public string Phone { get; set; }
    }

    /// <summary>
    /// One event item and the rules for its occurrences.
    /// </summary>
    internal sealed class CalendarEvent
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// One of the sample events Rock ships, updated in place.
        /// </summary>
        public bool RockEvent { get; set; }

        public List<string> Calendars { get; set; } = new List<string>();

        public List<string> Audiences { get; set; } = new List<string>();

        public string Summary { get; set; }

        /// <summary>
        /// The description, one paragraph per entry.
        /// </summary>
        public List<string> Description { get; set; } = new List<string>();

        /// <summary>
        /// "all", "church-wide", or a list of campus keys.
        /// </summary>
        public JsonElement Campuses { get; set; }

        public string Location { get; set; }

        /// <summary>
        /// The location of a step-season event, by season key.
        /// </summary>
        public Dictionary<string, string> LocationBySeason { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// A contact key, or "campus-pastor".
        /// </summary>
        public string Contact { get; set; }

        public EventWhen When { get; set; }

        /// <summary>
        /// "services", or a list of start times ("18:30").
        /// </summary>
        public JsonElement Times { get; set; }

        public Dictionary<string, List<string>> TimesByCampus { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// How long each start lasts. A span event uses <see cref="EndTime"/> instead.
        /// </summary>
        public int Minutes { get; set; }

        /// <summary>
        /// The event runs from its first day's start to its last day's <see cref="EndTime"/>, as one block.
        /// </summary>
        public bool Span { get; set; }

        /// <summary>
        /// How many days in a row each date runs. A step-season event takes its days from the season.
        /// </summary>
        public int Days { get; set; } = 1;

        public string EndTime { get; set; }

        /// <summary>
        /// Cast stories this event must agree with, checked after every load.
        /// </summary>
        public List<CalendarExpectation> Expect { get; set; } = new List<CalendarExpectation>();

        public bool IsChurchWide => Campuses.ValueKind == JsonValueKind.String && Campuses.GetString() == "church-wide";

        /// <summary>
        /// The campuses this event meets at, of those the dataset has, in the dataset's order. Empty for a church-wide event.
        /// </summary>
        public List<string> CampusKeys( IEnumerable<string> datasetCampuses )
        {
            if ( Campuses.ValueKind == JsonValueKind.String )
            {
                return Campuses.GetString() switch
                {
                    "all" => datasetCampuses.ToList(),
                    "church-wide" => new List<string>(),
                    var other => throw new InvalidDataException( $"configuration/calendar.json: event {Key} has campuses \"{other}\"; use \"all\", \"church-wide\", or a list." )
                };
            }

            var listed = Campuses.EnumerateArray().Select( c => c.GetString() ).ToHashSet();
            return datasetCampuses.Where( listed.Contains ).ToList();
        }

        /// <summary>
        /// The listed campus keys, for checks. Null for "all" and "church-wide".
        /// </summary>
        public List<string> ListedCampuses => Campuses.ValueKind == JsonValueKind.Array ? Campuses.EnumerateArray().Select( c => c.GetString() ).ToList() : null;

        /// <summary>
        /// True when each start is the campus's Sunday service times.
        /// </summary>
        public bool IsAtServices => Times.ValueKind == JsonValueKind.String && Times.GetString() == "services";

        /// <summary>
        /// The start times at a campus, when they are not the service times.
        /// </summary>
        public List<TimeSpan> StartTimes( string campusKey )
        {
            if ( campusKey != null && TimesByCampus.TryGetValue( campusKey, out var own ) )
            {
                return own.Select( TimeSpan.Parse ).ToList();
            }

            if ( Times.ValueKind != JsonValueKind.Array )
            {
                throw new InvalidDataException( $"configuration/calendar.json: event {Key} needs \"services\" or a list of times." );
            }

            return Times.EnumerateArray().Select( t => TimeSpan.Parse( t.GetString() ) ).ToList();
        }
    }

    /// <summary>
    /// A Cast story an event must agree with: a start at the campus between min and max days before the install day.
    /// </summary>
    internal sealed class CalendarExpectation
    {
        /// <summary>
        /// The campus key, or null for a church-wide occurrence.
        /// </summary>
        public string Campus { get; set; }

        public CampaignRange DaysAgo { get; set; }

        public string Story { get; set; }
    }

    /// <summary>
    /// When an event happens. <see cref="Kind"/> says which other fields apply (docs/calendar.md).
    /// </summary>
    internal sealed class EventWhen
    {
        /// <summary>
        /// weekly, monthly, yearly, date, easter, occasion, class, step-season, seasons, or pinned.
        /// </summary>
        public string Kind { get; set; }

        public string Weekday { get; set; }

        public int Month { get; set; }

        /// <summary>
        /// Which such weekday of the month, from 1; -1 is the last.
        /// </summary>
        public int Week { get; set; }

        public int Day { get; set; }

        /// <summary>
        /// Several yearly dates, instead of one month and week.
        /// </summary>
        public List<MonthWeek> Dates { get; set; }

        /// <summary>
        /// For a yearly event, how many weeks in a row it meets from each date.
        /// </summary>
        public int Weeks { get; set; } = 1;

        /// <summary>
        /// Days from Easter (easter), or from the occasion's date (occasion).
        /// </summary>
        public int OffsetDays { get; set; }

        public string Occasion { get; set; }

        public string StepType { get; set; }

        /// <summary>
        /// A step-season event's one season, or null for every season.
        /// </summary>
        public string Season { get; set; }

        public List<EventWhenSeason> Seasons { get; set; }

        /// <summary>
        /// A pinned date: days before the install day (negative is after), moved to <see cref="Weekday"/> in <see cref="Direction"/>.
        /// </summary>
        public int DaysAgo { get; set; }

        /// <summary>
        /// "before" or "after": which way a pinned date moves to its weekday.
        /// </summary>
        public string Direction { get; set; } = "before";

        /// <summary>
        /// A pinned date repeats "yearly" (the same distance from the install day each year) or every <see cref="EveryWeeks"/> "weeks".
        /// </summary>
        public string Repeat { get; set; }

        public int EveryWeeks { get; set; } = 1;
    }

    /// <summary>
    /// One season of a weekly event, from one day of the year to another, inclusive.
    /// </summary>
    internal sealed class EventWhenSeason
    {
        public MonthWeek From { get; set; }

        public MonthWeek Until { get; set; }
    }

    /// <summary>
    /// The contents of photos/events/manifest.json: the delivered event photos.
    /// </summary>
    internal sealed class EventPhotoManifest
    {
        public List<EventPhotoEntry> Images { get; set; } = new List<EventPhotoEntry>();
    }

    internal sealed class EventPhotoEntry
    {
        /// <summary>
        /// The file under photos/, for example "events/easter.jpg".
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// The event's key in calendar.json.
        /// </summary>
        public string Event { get; set; }
    }
}
