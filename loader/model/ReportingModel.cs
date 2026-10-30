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
    /// configuration/reporting.json: the church's layered data views (docs/reporting.md), the schedules that persist
    /// them, and who should be able to see them.
    /// </summary>
    internal sealed class ReportingConfiguration
    {
        public List<NamedPersistSchedule> NamedSchedules { get; set; } = new List<NamedPersistSchedule>();

        public List<DataViewDefinition> DataViews { get; set; } = new List<DataViewDefinition>();

        public List<ReportDefinition> Reports { get; set; } = new List<ReportDefinition>();

        /// <summary>
        /// Rock Metrics that count a data view week by week.
        /// </summary>
        public List<MetricDefinition> Metrics { get; set; } = new List<MetricDefinition>();

        /// <summary>
        /// The security walkthrough as expectations: what Rock's own security check must answer for a Cast person.
        /// Verify asks Rock each one.
        /// </summary>
        public List<AccessExpectation> Access { get; set; } = new List<AccessExpectation>();
    }

    /// <summary>
    /// A named schedule in Rock's Persisted DataView Schedules category, which a persisted data view can pick, or in
    /// Rock's Metrics schedule category, which a metric can pick.
    /// </summary>
    internal sealed class NamedPersistSchedule
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The time of day it runs, such as "02:00".
        /// </summary>
        public string Time { get; set; }

        /// <summary>
        /// The day of the week it runs, such as Monday, or null to run every day.
        /// </summary>
        public string Weekday { get; set; }

        /// <summary>
        /// The weekday as a day, or null to run every day.
        /// </summary>
        public DayOfWeek? Day => Weekday == null ? null : ( DayOfWeek ) Enum.Parse( typeof( DayOfWeek ), Weekday, true );

        /// <summary>
        /// Whether it is filed with Rock's metric schedules instead of the persisted data view schedules.
        /// </summary>
        public bool ForMetrics { get; set; }
    }

    /// <summary>
    /// A Rock Metric whose value is a data view's count, taken on a schedule.
    /// </summary>
    internal sealed class MetricDefinition
    {
        public string Key { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public string IconCssClass { get; set; }

        /// <summary>
        /// The key of its metric category in configuration/categories.json.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// The key of the data view it counts, in configuration/reporting.json.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// The key of the named schedule it runs on.
        /// </summary>
        public string Schedule { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The Guid of its one partition, the default one Rock adds to a metric that is not split by campus or anything else.
        /// </summary>
        public Guid PartitionGuid { get; set; }
    }

    /// <summary>
    /// One report: the data view behind it, and its columns in order.
    /// </summary>
    internal sealed class ReportDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The key of its report category in configuration/categories.json.
        /// </summary>
        public string Category { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The key of the data view behind it, in any configuration file. The report returns that view's entity.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// Rules on this report alone, only where it must be narrower than its category.
        /// </summary>
        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();

        public List<ReportColumnDefinition> Columns { get; set; } = new List<ReportColumnDefinition>();

        /// <summary>
        /// Its run history, only where it must differ from the usual rule, or null.
        /// </summary>
        public UsageHistory Usage { get; set; }
    }

    /// <summary>
    /// A data view or report's run history, as Rock records it: when it was made, how many times it has run since,
    /// and when it last ran. Each value left out follows the usual rule (docs/reporting.md).
    /// </summary>
    internal sealed class UsageHistory
    {
        public int? CreatedDaysAgo { get; set; }

        public int? Runs { get; set; }

        public int? LastRunDaysAgo { get; set; }
    }

    /// <summary>
    /// One report column, one of four kinds: a property of the record, an attribute, a Rock data select component, or a
    /// Lava template (Rock's LiquidSelect component).
    /// </summary>
    internal sealed class ReportColumnDefinition
    {
        /// <summary>
        /// The column header.
        /// </summary>
        public string Header { get; set; }

        /// <summary>
        /// A property of the record, such as Email.
        /// </summary>
        public string Property { get; set; }

        /// <summary>
        /// An attribute of the record's entity, by key, such as BaptismDate.
        /// </summary>
        public string Attribute { get; set; }

        /// <summary>
        /// A Rock data select component, by class name, with its selection.
        /// </summary>
        public string Component { get; set; }

        public string Selection { get; set; }

        /// <summary>
        /// For a component whose selection names a data view: the view's key, which fills a {dataView} placeholder.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// A Lava template, run for each row.
        /// </summary>
        public string Lava { get; set; }

        /// <summary>
        /// Sorts the report by this column: 1 sorts first. Null does not sort by it.
        /// </summary>
        public int? Sort { get; set; }

        public bool Descending { get; set; }
    }

    /// <summary>
    /// One answer Rock's security check must give: whether a Cast person may take an action on a category or data view.
    /// </summary>
    internal sealed class AccessExpectation
    {
        /// <summary>
        /// The Cast person's Guid.
        /// </summary>
        public Guid Person { get; set; }

        /// <summary>
        /// The person's name, for messages.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A category's key in configuration/categories.json, or null when the item is not a category.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// A data view's key, in any configuration file, or null when the item is not a data view.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// A report's key, or null when the item is not a report.
        /// </summary>
        public string Report { get; set; }

        /// <summary>
        /// Rock's action, such as View or Edit.
        /// </summary>
        public string Action { get; set; }

        public bool Allowed { get; set; }
    }
}
