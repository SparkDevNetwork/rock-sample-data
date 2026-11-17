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

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// configuration/person-attributes.json: the person attributes Rock ships that the sample data fills, the Date
    /// Engaged attribute the loader adds, and the rules that fill them for generated people.
    /// </summary>
    internal sealed class PersonAttributesConfiguration
    {
        /// <summary>
        /// Every Rock person attribute the sample data reads or writes, or that a Cast file holds. Every load checks that
        /// Rock has each one under its key.
        /// </summary>
        public List<RockPersonAttribute> RockAttributes { get; set; } = new List<RockPersonAttribute>();

        /// <summary>
        /// The attribute keys the people step's Cast writer loads from Cast files, as Rock saves each value.
        /// </summary>
        public List<string> CastLoaded { get; set; } = new List<string>();

        /// <summary>
        /// Attribute keys nothing in the sample data writes, with the reason in the file.
        /// </summary>
        public List<string> Excluded { get; set; } = new List<string>();

        public DateEngagedDefinition DateEngaged { get; set; }

        public SchoolRules Schools { get; set; }

        public EmploymentRules Employment { get; set; }

        public LegalNotesRules LegalNotes { get; set; }

        public AbilityLevelRules AbilityLevel { get; set; }

        public VisitRules Visit { get; set; }

        public EnvelopeRules Envelopes { get; set; }

        /// <summary>
        /// The Guid of the Rock attribute with this key. Throws when the file does not list it.
        /// </summary>
        public Guid AttributeGuid( string key )
        {
            return RockAttributes.FirstOrDefault( a => a.Key == key )?.Guid
                ?? throw new InvalidDataException( $"configuration/person-attributes.json does not list the Rock person attribute {key}." );
        }
    }

    /// <summary>
    /// A person attribute Rock ships.
    /// </summary>
    internal sealed class RockPersonAttribute
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// The Date Engaged attribute the loader adds to Rock's Membership category.
    /// </summary>
    internal sealed class DateEngagedDefinition
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The person attribute category it goes in: Rock's Membership.
        /// </summary>
        public Guid Category { get; set; }

        public int Order { get; set; }

        public Dictionary<string, string> Qualifiers { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The connection status keys whose adults get a date.
        /// </summary>
        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>
        /// For an adult with no small group, serving team, or gift: the date is this many years before the history
        /// window starts, at most, and a day after it at least (see the file's comment).
        /// </summary>
        public int YearsBeforeHistoryWithoutRecords { get; set; }
    }

    /// <summary>
    /// Invented schools, added to Rock's School defined type, for generated kids and students.
    /// </summary>
    internal sealed class SchoolRules
    {
        /// <summary>
        /// Rock's School defined type.
        /// </summary>
        public Guid DefinedType { get; set; }

        /// <summary>
        /// The share of generated households with kids in school that record each kid's school, from 0 to 100.
        /// </summary>
        public double HouseholdPercent { get; set; }

        public List<SchoolBand> Bands { get; set; } = new List<SchoolBand>();

        /// <summary>
        /// Each campus's schools, by campus key.
        /// </summary>
        public Dictionary<string, List<SchoolDefinition>> Campuses { get; set; } = new Dictionary<string, List<SchoolDefinition>>();
    }

    /// <summary>
    /// A grade band, such as elementary, kindergarten (0) to 5th grade.
    /// </summary>
    internal sealed class SchoolBand
    {
        public string Key { get; set; }

        public int MinGrade { get; set; }

        public int MaxGrade { get; set; }
    }

    internal sealed class SchoolDefinition
    {
        public string Name { get; set; }

        public string Band { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// Employer and Position for working-age generated adults.
    /// </summary>
    internal sealed class EmploymentRules
    {
        /// <summary>
        /// The share of working-age generated adults with an employer, besides business contacts, from 0 to 100.
        /// </summary>
        public double Percent { get; set; }

        public int MinAge { get; set; }

        public int MaxAge { get; set; }

        /// <summary>
        /// A business contact's Position, by their place among the business's contacts: the first, then a spouse.
        /// </summary>
        public List<string> ContactPositions { get; set; } = new List<string>();

        public List<EmployerDefinition> Employers { get; set; } = new List<EmployerDefinition>();
    }

    internal sealed class EmployerDefinition
    {
        public string Name { get; set; }

        public List<string> Positions { get; set; } = new List<string>();
    }

    /// <summary>
    /// Pickup and custody notes for a few kids in single-parent generated households at each campus.
    /// </summary>
    internal sealed class LegalNotesRules
    {
        public int HouseholdsPerCampus { get; set; }

        /// <summary>
        /// The oldest a kid can be and still get the note: kids who check in with their family.
        /// </summary>
        public int MaxAge { get; set; }

        /// <summary>
        /// Notes by the household adult's gender (female or male). They name roles, never people.
        /// </summary>
        public Dictionary<string, List<string>> Notes { get; set; } = new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// Ability Level for some generated kids in the nursery and preschool rooms.
    /// </summary>
    internal sealed class AbilityLevelRules
    {
        /// <summary>
        /// The share of those kids with an ability level, from 0 to 100.
        /// </summary>
        public double Percent { get; set; }

        /// <summary>
        /// The check-in groups, by key in configuration/checkin.json, whose Rock group filters by ability level.
        /// </summary>
        public List<string> Groups { get; set; } = new List<string>();
    }

    /// <summary>
    /// Source of Visit and Previous Church for generated households that first came inside the history window.
    /// </summary>
    internal sealed class VisitRules
    {
        /// <summary>
        /// The share of those households with a Source of Visit, from 0 to 100.
        /// </summary>
        public double SourcePercent { get; set; }

        /// <summary>
        /// Weights by Rock's Source of Visit defined value Guid.
        /// </summary>
        public Dictionary<string, double> Sources { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The share of those households with a Previous Church, from 0 to 100.
        /// </summary>
        public double PreviousChurchPercent { get; set; }

        /// <summary>
        /// Descriptions of a church, never a church's name.
        /// </summary>
        public List<string> PreviousChurches { get; set; } = new List<string>();
    }

    /// <summary>
    /// Envelope Number for a share of check writers.
    /// </summary>
    internal sealed class EnvelopeRules
    {
        /// <summary>
        /// The share of people who wrote a check gift with an envelope number, from 0 to 100.
        /// </summary>
        public double Percent { get; set; }

        public int FirstNumber { get; set; }

        /// <summary>
        /// The largest step between two numbers, so the numbers have the gaps a church's list has.
        /// </summary>
        public int MaxStep { get; set; }
    }
}
