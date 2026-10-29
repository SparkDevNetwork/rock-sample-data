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

namespace RockSampleData.Loader
{
    /// <summary>
    /// The parts of a dataset a person can choose. Church setup and people always load.
    /// <see cref="SampleDataParts.All"/> names and describes each one and says what it needs.
    /// </summary>
    public enum SampleDataPart
    {
        /// <summary>
        /// Profile photos and serving opportunity photos.
        /// </summary>
        Photos,

        /// <summary>
        /// Sunday attendance and kids check-in, with check-in streaks and achievements.
        /// </summary>
        Attendance,

        /// <summary>
        /// Small groups with members, meeting places, photos, history, and weekly attendance.
        /// </summary>
        SmallGroups,

        /// <summary>
        /// Serving team volunteers, their schedules, and requests to serve.
        /// </summary>
        Serving,

        /// <summary>
        /// Gifts, scheduled gifts, batches, giving analytics, giving alerts, and the building campaign's pledges.
        /// </summary>
        Giving,

        /// <summary>
        /// Scanned images of recent check gifts.
        /// </summary>
        CheckImages,

        /// <summary>
        /// Prayer requests and the prayer team's activity.
        /// </summary>
        Prayer,

        /// <summary>
        /// Notes on people.
        /// </summary>
        Notes,

        /// <summary>
        /// Steps on Rock's Discipleship Path and the Student Path, with baptism dates and program completions.
        /// </summary>
        Steps,

        /// <summary>
        /// Two years of email, texts, and pushes, with the lists, numbers, template, app, and flow they use.
        /// </summary>
        Communications,

        /// <summary>
        /// Events on the Public and Internal calendars, with their occurrences and photos.
        /// </summary>
        Calendar,

        /// <summary>
        /// Event registrations linked to calendar events, with their templates, fees, discounts, and groups.
        /// </summary>
        Registrations
    }

    /// <summary>
    /// One part of a dataset: its name, what it adds, and the parts it needs.
    /// </summary>
    public sealed class SampleDataPartInfo
    {
        internal SampleDataPartInfo( SampleDataPart part, string name, string description, params SampleDataPart[] dependsOn )
        {
            Part = part;
            Name = name;
            Description = description;
            DependsOn = dependsOn;
        }

        /// <summary>
        /// The part.
        /// </summary>
        public SampleDataPart Part { get; }

        /// <summary>
        /// The name to show a person, for example "Attendance and check-in".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// One plain sentence that says what the part adds.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The parts this part needs. Choosing this part loads them too.
        /// </summary>
        public IReadOnlyList<SampleDataPart> DependsOn { get; }
    }

    /// <summary>
    /// Every part, with what each needs (decision 121).
    /// </summary>
    public static class SampleDataParts
    {
        /*
            10/21/26 - CLAUDE

            A part depends on another only when its rows point at or must agree with that part's
            rows. Parts that only read another part's plan do not, because the loader plans everything
            in memory and skips only the writes, so a skipped part never changes another part's rows.
            Serving and giving read the attendance plan, and prayer reads the giving plan, so none of
            them needs those parts.

            Check images are pictures of check gifts: each image row points at its gift. Prayer needs
            small groups: some Cast prayer requests are shared with a small group, and who asks for
            prayer depends on who is in a group. Group photos belong to small groups and serving
            opportunity photos to photos, so no other part's choice changes the photos part's rows.

            Steps need small groups, serving, and giving. A Small Group, Serve, or Lead step copies one
            small group or team membership, read back from the rows those parts wrote, and points at its
            group. People with an open request to serve come from the serving part's connection requests. A
            Give step must agree with the journey stage the giving part writes. Steps read the attendance
            plan only, so they do not need attendance.

            Communications need small groups, serving, prayer, and steps. The Volunteers and Small Group Leaders
            lists copy the team and group memberships those parts wrote, and the Prayer Team list holds the
            prayer part's team. Class emails and texts reach the people in each Starting Point Class and
            Foundations course, baptisms get a follow-up, and the After Starting Point flow follows each
            completed class, all read back from the steps part's rows. Who is involved enough for a newsletter reads the attendance and giving
            plans only, so communications do not need those parts.

            Registrations need the calendar only (decision 189). Every linkage points at a calendar
            occurrence, found through the calendar's plan, so the calendar must have written it. Payments are
            their own rows, so registrations do not need giving. Students register from the Student Path plan, which
            every load makes, not from the steps part's rows, so last summer's campers match the camp steps without
            registrations needing steps (decision 206).

            Reason: One table holds the parts, so the loader, the CLI, and Rock's block agree.
        */

        /// <summary>
        /// Every part, in the order the loader loads them.
        /// </summary>
        public static IReadOnlyList<SampleDataPartInfo> All { get; } = new[]
        {
            new SampleDataPartInfo( SampleDataPart.Photos, "Photos", "Profile photos for the Cast and some generated families, and photos for the serving opportunities." ),
            new SampleDataPartInfo( SampleDataPart.Attendance, "Attendance and check-in", "Sunday attendance and kids check-in over the whole history, with check-in streaks and achievements." ),
            new SampleDataPartInfo( SampleDataPart.SmallGroups, "Small groups", "Small groups with leaders, members, meeting places, photos, history, and weekly attendance." ),
            new SampleDataPartInfo( SampleDataPart.Serving, "Serving teams", "Volunteers on every serving team, with schedules, check-ins, and requests to serve." ),
            new SampleDataPartInfo( SampleDataPart.Giving, "Giving", "Gifts, scheduled gifts, and batches, with giving analytics, a year of giving alerts, and the building campaign's pledges." ),
            new SampleDataPartInfo( SampleDataPart.Prayer, "Prayer requests", "Prayer requests with the prayer team's prayers, comments, and answers.", SampleDataPart.SmallGroups ),
            new SampleDataPartInfo( SampleDataPart.Notes, "Notes", "Notes on people in six note types, secured to staff." ),
            new SampleDataPartInfo( SampleDataPart.Steps, "Steps", "Steps on Rock's Discipleship Path and the Student Path, from baptism and classes to small groups, serving, giving, leading, camps, and mission trips, with program completions.", SampleDataPart.SmallGroups, SampleDataPart.Serving, SampleDataPart.Giving ),
            new SampleDataPartInfo( SampleDataPart.Communications, "Communications", "Two years of email, texts, and pushes: weekly newsletters, mass and class messages, SMS conversations, serving reminders, the church's app, and a finished communication flow, with the lists, numbers, and template they use.", SampleDataPart.SmallGroups, SampleDataPart.Serving, SampleDataPart.Prayer, SampleDataPart.Steps ),
            new SampleDataPartInfo( SampleDataPart.Calendar, "Calendar events", "Events on the Public and Internal calendars: holidays, classes, student and kids events, men's and women's events, outreach, and staff meetings, with their occurrences at each campus and their photos." ),
            new SampleDataPartInfo( SampleDataPart.Registrations, "Event registrations", "Registrations for camp, the mission trip, the fall retreat, VBS, Parents' Night Out, Serve Day, Money Matters, Date Night, and the Women's Retreat, linked to their calendar events and groups, with fees, discounts, and placement groups, and the students who registered for camp, the mission trip, and the fall retreat, with their payments, payment plans, and placements.", SampleDataPart.Calendar ),
            new SampleDataPartInfo( SampleDataPart.CheckImages, "Check images", "Scanned front and back images of recent check gifts. This is the slowest part.", SampleDataPart.Giving )
        };

        /// <summary>
        /// The chosen parts plus every part they need, in load order.
        /// </summary>
        public static IReadOnlyCollection<SampleDataPart> WithDependencies( IEnumerable<SampleDataPart> parts )
        {
            var chosen = new HashSet<SampleDataPart>();
            var pending = new Stack<SampleDataPart>( parts );
            while ( pending.Count > 0 )
            {
                var part = pending.Pop();
                if ( chosen.Add( part ) )
                {
                    foreach ( var needed in Info( part ).DependsOn )
                    {
                        pending.Push( needed );
                    }
                }
            }

            return All.Select( i => i.Part ).Where( chosen.Contains ).ToList();
        }

        /// <summary>
        /// The row for one part.
        /// </summary>
        internal static SampleDataPartInfo Info( SampleDataPart part )
        {
            return All.FirstOrDefault( i => i.Part == part ) ?? throw new ArgumentOutOfRangeException( nameof( part ), part, "Not a sample data part." );
        }
    }
}
