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
using System;
using System.Collections.Generic;

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// configuration/church-app.json: the church's mobile app, the devices it runs on, and how people open its pushes.
    /// </summary>
    internal sealed class ChurchAppConfiguration
    {
        public ChurchAppSite Site { get; set; }

        public List<ChurchAppLayout> Layouts { get; set; } = new List<ChurchAppLayout>();

        public ChurchAppPage Homepage { get; set; }

        public ChurchAppDevices Devices { get; set; }

        public ChurchAppPush Push { get; set; }
    }

    internal sealed class ChurchAppSite
    {
        public string Name { get; set; }

        public string StoreName { get; set; }

        public Guid Guid { get; set; }

        public string Description { get; set; }

        public string BundleIdentifier { get; set; }

        public string PackageName { get; set; }

        public string DeepLinkDomains { get; set; }

        /// <summary>
        /// How long before the install day the app first went out.
        /// </summary>
        public int LaunchedYearsAgo { get; set; }
    }

    internal sealed class ChurchAppLayout
    {
        public string Name { get; set; }

        public string FileName { get; set; }

        public Guid Guid { get; set; }
    }

    internal sealed class ChurchAppPage
    {
        public Guid Guid { get; set; }
    }

    internal sealed class ChurchAppDevices
    {
        public double PctOfActiveAdults { get; set; }

        public double PctIos { get; set; }

        public double PctNotificationsOn { get; set; }

        public double PctSeenInLast30Days { get; set; }

        public Dictionary<string, Guid> Platforms { get; set; } = new Dictionary<string, Guid>();

        public List<string> IosModels { get; set; } = new List<string>();

        public List<ChurchAppAndroidModel> AndroidModels { get; set; } = new List<ChurchAppAndroidModel>();
    }

    internal sealed class ChurchAppAndroidModel
    {
        public string Manufacturer { get; set; }

        public string Model { get; set; }
    }

    internal sealed class ChurchAppPush
    {
        public double PctOpened { get; set; }

        public double PctOpenedWithinHour { get; set; }
    }
}
