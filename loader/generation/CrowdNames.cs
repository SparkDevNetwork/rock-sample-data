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

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// Spike-only name lists for the crowd (and placeholder streets for when the address pool runs out). The real Crowd uses the Content name lists.
    /// Names are common across many backgrounds and are picked independently of anything else.
    /// </summary>
    internal static class CrowdNames
    {
        public static readonly string[] FemaleFirstNames =
        {
            "Abigail", "Amara", "Amy", "Ana", "Angela", "Aria", "Ava", "Brenda", "Camila", "Carmen", "Chloe", "Claire", "Dana", "Deborah",
            "Elena", "Ella", "Emily", "Emma", "Erin", "Fatima", "Grace", "Hannah", "Isabel", "Jada", "Jasmine", "Jennifer", "Julia", "Karen",
            "Keisha", "Laura", "Leah", "Lily", "Linda", "Lucia", "Maria", "Maya", "Megan", "Mia", "Nadia", "Naomi", "Nicole", "Olivia",
            "Priya", "Rachel", "Rosa", "Ruth", "Sara", "Sofia", "Susan", "Tanya", "Tessa", "Valerie", "Vanessa", "Wendy", "Yesenia", "Zoe"
        };

        public static readonly string[] MaleFirstNames =
        {
            "Aaron", "Adrian", "Alex", "Andre", "Andrew", "Anthony", "Ben", "Brandon", "Caleb", "Carlos", "Chris", "Daniel", "David", "Diego",
            "Dominic", "Eli", "Eric", "Ethan", "Gabriel", "Greg", "Hector", "Isaac", "Jacob", "James", "Jason", "Javier", "Jordan", "Jose",
            "Joshua", "Kevin", "Liam", "Lucas", "Luis", "Malik", "Marcus", "Mark", "Mateo", "Michael", "Nathan", "Noah", "Omar", "Owen",
            "Paul", "Raj", "Ricardo", "Robert", "Ryan", "Samuel", "Scott", "Steven", "Thomas", "Tyler", "Victor", "Wesley", "Xavier", "Zach"
        };

        public static readonly string[] LastNames =
        {
            "Adams", "Allen", "Alvarez", "Bailey", "Baker", "Bell", "Brooks", "Brown", "Campbell", "Carter", "Castillo", "Chen", "Clark",
            "Collins", "Cook", "Cruz", "Davis", "Diaz", "Edwards", "Evans", "Fisher", "Flores", "Foster", "Garcia", "Gomez", "Gray", "Green",
            "Hall", "Harris", "Hayes", "Hernandez", "Hill", "Hughes", "Jackson", "James", "Jenkins", "Johnson", "Kelly", "Kim", "King", "Lee",
            "Lewis", "Lopez", "Martin", "Martinez", "Miller", "Mitchell", "Moore", "Morgan", "Morris", "Murphy", "Nelson", "Nguyen", "Ortiz",
            "Parker", "Patel", "Perez", "Phillips", "Price", "Ramirez", "Reed", "Reyes", "Rivera", "Roberts", "Robinson", "Rogers", "Ross",
            "Russell", "Sanchez", "Scott", "Shah", "Smith", "Stewart", "Sullivan", "Taylor", "Thomas", "Thompson", "Torres", "Turner", "Walker",
            "Ward", "Watson", "White", "Williams", "Wilson", "Wood", "Wright", "Young"
        };

        /// <summary>
        /// Spike-only streets. Deliberately invented; the real Crowd draws from the address pool.
        /// </summary>
        public static readonly string[] Streets =
        {
            "Spike Test Ln", "Spike Test Dr", "Spike Test Ave", "Spike Test Way", "Spike Test Ct", "Spike Test Pl"
        };
    }
}
