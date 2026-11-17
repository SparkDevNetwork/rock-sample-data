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
    /// Rock's five assessment scoring tables, copied from Rock's services in Rock/Model/CRM/Assessment. Rock 20.0.10 and 21.0.2
    /// have the same tables. Each number is Rock's own literal, so these are the same doubles Rock computes with.
    /// </summary>
    internal static class RockAssessmentTables
    {
        /// <summary>
        /// Rock's zScoreToPercentage, the same in all five services, including the 0.9 that repeats 0.8's 78.8.
        /// </summary>
        public static readonly Dictionary<double, double> ZScoreToPercentage = new Dictionary<double, double>
        {
            { 3, 99.9 },
            { 2.9, 99.8 },
            { 2.8, 99.7 },
            { 2.7, 99.6 },
            { 2.6, 99.5 },
            { 2.5, 99.4 },
            { 2.4, 99.2 },
            { 2.3, 98.9 },
            { 2.2, 98.6 },
            { 2.1, 98.2 },
            { 2, 97.8 },
            { 1.9, 97.1 },
            { 1.8, 96.4 },
            { 1.7, 95.5 },
            { 1.6, 94.5 },
            { 1.5, 93.3 },
            { 1.4, 91.9 },
            { 1.3, 90.3 },
            { 1.2, 88.5 },
            { 1.1, 86.4 },
            { 1, 84.1 },
            { 0.9, 78.8 },
            { 0.8, 78.8 },
            { 0.7, 75.8 },
            { 0.6, 72.6 },
            { 0.5, 69.2 },
            { 0.4, 65.5 },
            { 0.3, 61.8 },
            { 0.2, 57.9 },
            { 0.1, 54 },
            { 0, 50 },
            { -0.1, 46 },
            { -0.2, 42.1 },
            { -0.3, 38.2 },
            { -0.4, 34.5 },
            { -0.5, 30.9 },
            { -0.6, 27.4 },
            { -0.7, 24.2 },
            { -0.8, 21.2 },
            { -0.9, 18.4 },
            { -1, 15.9 },
            { -1.1, 13.6 },
            { -1.2, 11.5 },
            { -1.3, 9.7 },
            { -1.4, 8.1 },
            { -1.5, 6.7 },
            { -1.6, 5.5 },
            { -1.7, 4.5 },
            { -1.8, 3.6 },
            { -1.9, 2.9 },
            { -2, 2.3 },
            { -2.1, 1.8 },
            { -2.2, 1.4 },
            { -2.3, 1.1 },
            { -2.4, 0.8 },
            { -2.5, 0.6 },
            { -2.6, 0.5 },
            { -2.7, 0.4 },
            { -2.8, 0.3 },
            { -2.9, 0.2 },
            { -3, 0.1 }
        };

        /// <summary>
        /// DiscService.questionData: each question's four responses, then the letters the responses score as Most and as Least.
        /// </summary>
        public static readonly string[,] DiscQuestions =
        {
            { "Follows rules", "Enjoys challenges", "Enjoys having fun", "Uncomfortable with change", "CDIS", "CDIS" },
            { "Tries to avoid mistakes", "Not easily moved", "Convinces others ", "Goes with the flow", "CDIS", "CDIS" },
            { "Thinks through issues", "Quickly shares thoughts", "Liked by others", "Prefers not to change", "CDIS", "CDIS" },
            { "Calculates actions", "Makes decisions quickly", "Enjoys talking", "Actions are predictable", "CDIS", "CDIS" },
            { "Sees imperfections", "Takes risks", "People person", "Avoids extremes", "CDIS", "CDIS" },
            { "Struggles acknowledging abilities", "Produces results", "Influences others", "Sensitive to others", "CDIS", "CDIS" },
            { "Avoids mistakes", "Takes control", "Full of energy", "Available to others", "CDIS", "CDIS" },
            { "Easily focuses", "Fully committed to the goal", "High-spirited", "Does not speak up", "CDIS", "CDIS" },
            { "Pays attention to details", "Gives direction", "Is entertaining", "Consistent with others", "CDIS", "CDIS" },
            { "Very exact", "Conquers challenges", "Encourages others", "Follows others", "CDIS", "CDIS" },
            { "Plays down abilities", "Driven by goals", "Is an optimist", "Concerned for others", "CDIS", "CDIS" },
            { "Concerned for correctness", "Is self sufficient", "Persuades others", "Concerned for others", "CDIS", "CDIS" },
            { "Avoids danger", "Pursues goals", "Are enjoyed by People", "Empathizes with others", "CDIS", "CDIS" },
            { "Prefers precision", "Starts things", "Enjoys life fully", "Follows others", "CDIS", "CDIS" },
            { "Thinks things through", "Perseveres obstacles", "Liked by others", "Sticks with the traditional way", "CDIS", "CDIS" },
            { "Is analytical", "Takes risks", "Possesses charm ", "Loyal to others", "CDIS", "CDIS" },
            { "Share details", "Unmovable in opposition", "Enjoys others", "Patient listener", "CDIS", "CDIS" },
            { "Works systematically", "Pursues doggedly", "Full of energy", "Doesn't challenge others", "CDIS", "CDIS" },
            { "Prone to worrying", "Overcomes difficulties", "Freely shows feelings", "Mindful of others", "CDIS", "CDIS" },
            { "Is concerned about the facts", "Secure in abilities", "Enjoys people", "Cares deeply about others", "CDIS", "CDIS" },
            { "Does things the right way", "Committed to the goal", "Shares inner thoughts", "Is generous", "CDIS", "CDIS" },
            { "Evaluates objectively", "Assumes control", "Acts without thinking", "Like things the way they are", "CDIS", "CDIS" },
            { "Systematically thinks through issues", "Displays confidence", "Spends time with others", "Gives grace to others", "CDIS", "CDIS" },
            { "Follows instructions precisely", "Resistant to opposition", "Seeks out others", "Comfortable with the status quo", "CDIS", "CDIS" },
            { "Thinks through problems", "Pushes others", "Upbeat about life", "Collaborates with others", "CDIS", "CDIS" },
            { "Conforms exactly to a standard", "Freely expresses opinions", "Enjoys having fun with others", "Is consistent", "CDIS", "CDIS" },
            { "Carefully assesses risks", "Forcefully pursues goals", "Welcomes others", "Considerate of others", "CDIS", "CDIS" },
            { "Critical of others", "Drives to complete the goal", "Optimistic toward others", "Concerned for others", "CDIS", "CDIS" },
            { "Tries to avoid mistakes", "Does not show weakness", "Enjoys others", "Readily follows others", "CDIS", "CDIS" },
            { "Follows rules", "Stands out", "Expresses feelings", "Satisfied with circumstances", "CDIS", "CDIS" }
        };

        /// <summary>
        /// DiscService.constructData: the mean and standard deviation of each adaptive and natural score.
        /// </summary>
        public static readonly (string Key, double Mean, double StandardDeviation)[] DiscConstants =
        {
            ("AdaptiveD", 11.65673864, 4.418053037),
            ("AdaptiveI", 13.16455696, 5.194907006),
            ("AdaptiveS", 11.93373045, 3.956836979),
            ("AdaptiveC", 13.24497394, 5.183690143),
            ("NaturalD", 11.76396128, 4.344015851),
            ("NaturalI", 13.3655994, 4.156180707),
            ("NaturalS", 12.38644825, 3.79901483),
            ("NaturalC", 12.48399106, 4.431075553)
        };

        /// <summary>
        /// SpiritualGiftsService.constructData, in Rock's order, which breaks ties: each gift's defined value, mean, standard deviation, and questions.
        /// </summary>
        public static readonly (Guid Gift, double Mean, double StandardDeviation, string[] Questions)[] SpiritualGifts =
        {
            (new Guid( "A276421D-F662-4723-99DA-6FDF3E9CFF7C" ), 14.26, 3.54, new[] { "C6", "C18", "C94", "C99", "C102" }),
            (new Guid( "A2C7074E-AC97-4D89-9240-47A552CDC4C0" ), 11.16, 4.025, new[] { "C5", "C8", "C26", "C56", "C96" }),
            (new Guid( "3EB352F3-F624-4ED6-A9EE-7951B71B1952" ), 14.15, 3.759, new[] { "C1", "C20", "C51", "C76", "C109" }),
            (new Guid( "809F65A6-1759-472A-8B8B-F37009F476BF" ), 14.17, 3.404, new[] { "C59", "C63", "C75", "C78", "C111" }),
            (new Guid( "0F8D41AA-7236-40BF-AA37-980BCCF4A881" ), 9.11, 4.391, new[] { "C80", "C112", "C114", "C117", "C118" }),
            (new Guid( "7B30E2BA-9461-4688-9B43-D2B774E33A18" ), 12.82, 3.684, new[] { "C42", "C57", "C106", "C108", "C113" }),
            (new Guid( "C4259D6E-675C-417B-9175-6D599C86A204" ), 13.36, 3.491, new[] { "C50", "C71", "C83", "C86", "C101" }),
            (new Guid( "13C40209-F41D-4C1D-83D3-2EC530588245" ), 14.02, 3.132, new[] { "C9", "C39", "C70", "C81", "C103" }),
            (new Guid( "98D5EE08-633D-4635-80CD-169449604D18" ), 10.56, 4.83, new[] { "C12", "C16", "C30", "C66", "C105" }),
            (new Guid( "462A5D10-6DEA-43D7-96EF-8F82FF1E2E14" ), 11.89, 4.172, new[] { "C64", "C69", "C72", "C85", "C119" }),
            (new Guid( "A1CB038C-AAFC-4745-A7D2-7C8BA5028F05" ), 12.55, 4.061, new[] { "C25", "C29", "C47", "C74", "C116" }),
            (new Guid( "0894EDBA-8FC8-4433-877C-53351A06A8B7" ), 13.3, 3.682, new[] { "C4", "C31", "C67", "C90", "C100" }),
            (new Guid( "FC4F1B46-F0C3-45B0-9FD9-D15F4FD05A31" ), 13.26, 3.12, new[] { "C13", "C14", "C15", "C22", "C61" }),
            (new Guid( "C7291F22-05F0-4EF9-A7C2-2CFEBFEBCB45" ), 13.33, 3.294, new[] { "C65", "C68", "C88", "C89", "C92" }),
            (new Guid( "4ADAEED1-D0E6-4DA4-A0BA-8E7D058075C4" ), 12.02, 3.518, new[] { "C7", "C19", "C35", "C41", "C60" }),
            (new Guid( "E8278791-2400-4DDA-AEAA-C6F11E0AC9D0" ), 12.36, 3.912, new[] { "C17", "C33", "C37", "C45", "C77" }),
            (new Guid( "5F1F5A92-D981-4027-A4BC-C3642E784D0B" ), 12.24, 3.613, new[] { "C3", "C21", "C58", "C62", "C87" })
        };

        /// <summary>
        /// SpiritualGiftsService.questionData's question codes, in Rock's order.
        /// </summary>
        public static readonly string[] SpiritualGiftQuestions = { "C1", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C12", "C13", "C14", "C15", "C16", "C17", "C18", "C19", "C20", "C21", "C22", "C25", "C26", "C29", "C30", "C31", "C33", "C35", "C37", "C39", "C41", "C42", "C45", "C47", "C50", "C51", "C56", "C57", "C58", "C59", "C60", "C61", "C62", "C63", "C64", "C65", "C66", "C67", "C68", "C69", "C70", "C71", "C72", "C74", "C75", "C76", "C77", "C78", "C80", "C81", "C83", "C85", "C86", "C87", "C88", "C89", "C90", "C92", "C94", "C96", "C99", "C100", "C101", "C102", "C103", "C105", "C106", "C108", "C109", "C111", "C112", "C113", "C114", "C116", "C117", "C118", "C119" };

        /// <summary>
        /// ConflictProfileService.constructData, in Rock's order: each mode's defined value, mean, standard deviation, and questions.
        /// </summary>
        public static readonly (Guid Mode, double Mean, double StandardDeviation, string[] Questions)[] ConflictModes =
        {
            (new Guid( "663B0F4A-DE1F-46BE-8BDD-D7C98863DDC4" ), 17.009, 2.69, new[] { "a01", "a03", "a04", "a06", "a07", "a08", "a09", "a10" }),
            (new Guid( "CF78D6B1-38AA-4FF7-9A4B-E900438FA85A" ), 21.061, 1.59, new[] { "c01", "c02", "c04", "c05", "c08", "c09", "c10", "c11" }),
            (new Guid( "DF7B1EB2-7E7E-4F91-BD26-C6DFD88E38DF" ), 22.193, 2.40, new[] { "r02", "r03", "r05", "r06", "r07", "r08", "r09", "r10" }),
            (new Guid( "56300095-86AD-43FE-98D2-50829E9223C2" ), 21.459, 2.90, new[] { "w01", "w03", "w04", "w05", "w06", "w07", "w09", "w10" }),
            (new Guid( "4AB06A6F-F5B1-4385-9365-199EA7969E50" ), 18.276, 2.22, new[] { "y01", "y02", "y03", "y05", "y06", "y07", "y08", "y11" })
        };

        /// <summary>
        /// ConflictProfileService.questionData's question codes, in Rock's order.
        /// </summary>
        public static readonly string[] ConflictQuestions = { "a01", "a03", "a04", "a06", "a07", "a08", "a09", "a10", "c01", "c02", "c04", "c05", "c08", "c09", "c10", "c11", "r02", "r03", "r05", "r06", "r07", "r08", "r09", "r10", "w01", "w03", "w04", "w05", "w06", "w07", "w09", "w10", "y01", "y02", "y03", "y05", "y06", "y07", "y08", "y11" };

        /// <summary>
        /// EQInventoryService.constructData, in Rock's order: each scale's person attribute, mean, standard deviation, and questions.
        /// </summary>
        public static readonly (Guid Scale, double Mean, double StandardDeviation, string[] Questions)[] EqScales =
        {
            (new Guid( "A5EFCE3E-EA41-4FEC-99F6-DD748A7D5BB5" ), 34.36, 4.209, new[] { "fsa02N", "fsa03", "fsa04N", "fsa05", "fsa06", "fsa07", "fsa08", "fsa09", "fsa10N" }),
            (new Guid( "149CD0CD-3CD6-44B6-8D84-A17A477A8978" ), 31.43, 4.305, new[] { "fsr01", "fsr02", "fsr03N", "fsr04", "fsr06N", "fsr07", "fsr08", "fsr09N", "fsr10N" }),
            (new Guid( "A6AF0BE5-E93A-49EB-AFEA-3520B7C41C78" ), 34.17, 4.160, new[] { "foa02", "foa03", "foa04", "foa05", "foa06", "foa07", "foa08N", "foa09", "foa10" }),
            (new Guid( "129C108E-CE61-4DFB-A9A8-1EBC3462022E" ), 33.66, 5.352, new[] { "for02", "for03", "for04", "for05", "for06", "for07", "for08", "for09", "for10" }),
            (new Guid( "B598BF9C-7A0C-467E-B467-13B40DAC9F8D" ), 20.18, 3.59, new[] { "fps01N", "fps02", "fps03N", "fps04N", "fps05N", "fps07N", "fps08N" }),
            (new Guid( "C3CB8FB5-34A2-48C8-B1FC-7CEBA670C1ED" ), 29.07, 3.478, new[] { "fes01", "fes02", "fes03N", "fes04", "fes05", "fes06N", "fes07", "fes08" })
        };

        /// <summary>
        /// EQInventoryService.questionData's question codes, in Rock's order.
        /// </summary>
        public static readonly string[] EqQuestions = { "fsa02N", "fsa03", "fsa04N", "fsa05", "fsa06", "fsa07", "fsa08", "fsa09", "fsa10N", "fsr01", "fsr02", "fsr03N", "fsr04", "fsr06N", "fsr07", "fsr08", "fsr09N", "fsr10N", "foa02", "foa03", "foa04", "foa05", "foa06", "foa07", "foa08N", "foa09", "foa10", "for02", "for03", "for04", "for05", "for06", "for07", "for08", "for09", "for10", "fps01N", "fps02", "fps03N", "fps04N", "fps05N", "fps07N", "fps08N", "fes01", "fes02", "fes03N", "fes04", "fes05", "fes06N", "fes07", "fes08" };

        /// <summary>
        /// MotivatorService.constructData, in Rock's order, which breaks ties: each motivator's defined value, mean, and standard deviation.
        /// </summary>
        public static readonly (Guid Motivator, double Mean, double StandardDeviation)[] Motivators =
        {
            (new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" ), 27.56, 4.441),
            (new Guid( "FFD7EF9C-5D68-40D2-A362-416B2D660D51" ), 17.06, 5.391),
            (new Guid( "FA70E27D-6642-4162-AF17-530F66B507E7" ), 21.82, 6.247),
            (new Guid( "C171D01E-C607-488B-A550-1E341081210B" ), 21.87, 4.126),
            (new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" ), 23.25, 6.58),
            (new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" ), 23.26, 5.144),
            (new Guid( "73087DD2-B892-4367-894F-8922477B2F10" ), 21.77, 3.475),
            (new Guid( "D84E58E4-87FC-4CEB-B83E-A2C6D186366C" ), 22.66, 5.157),
            (new Guid( "6A2354C6-3FA4-4BAD-89A8-7359FEC48FE3" ), 22.52, 4.657),
            (new Guid( "7EA44A56-58CB-4E40-9779-CC0A79772926" ), 24.37, 5.192),
            (new Guid( "3F678404-5844-494F-BDB0-DD9FEEBC98C9" ), 21.59, 4.161),
            (new Guid( "85459C0F-65A5-48F9-86F3-40B03F9C53E9" ), 16.84, 4.802),
            (new Guid( "9F771853-2EBA-47A2-9AC5-26EBEA0A3B25" ), 20.09, 5.636),
            (new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" ), 27.75, 4.365),
            (new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" ), 28.92, 4.302),
            (new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" ), 25.97, 5.187),
            (new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" ), 27.93, 4.418),
            (new Guid( "2393C3CE-8E49-46FE-A75B-D5D624A37B49" ), 21.5, 4.556),
            (new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" ), 26.37, 5.22),
            (new Guid( "A027F6B2-56DD-4724-962D-F865606AEAB8" ), 20.53, 4.715),
            (new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" ), 24.83, 6.619),
            (new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" ), 30.03, 5.883),
            (new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" ), 32.63, 4.728)
        };

        /// <summary>
        /// MotivatorService.questionData, in Rock's order: each question's code and the motivator it scores.
        /// </summary>
        public static readonly (string Code, Guid Motivator)[] MotivatorQuestions =
        {
            ("f0101", new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" )),
            ("f0102", new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" )),
            ("f0103", new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" )),
            ("f0104", new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" )),
            ("f0105", new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" )),
            ("f0106", new Guid( "99F598E0-E0AC-4B4B-BEAF-589D41764EE1" )),
            ("f0201", new Guid( "FFD7EF9C-5D68-40D2-A362-416B2D660D51" )),
            ("f0202", new Guid( "FFD7EF9C-5D68-40D2-A362-416B2D660D51" )),
            ("f0203", new Guid( "FFD7EF9C-5D68-40D2-A362-416B2D660D51" )),
            ("f0204", new Guid( "FFD7EF9C-5D68-40D2-A362-416B2D660D51" )),
            ("f0301", new Guid( "FA70E27D-6642-4162-AF17-530F66B507E7" )),
            ("f0302", new Guid( "FA70E27D-6642-4162-AF17-530F66B507E7" )),
            ("f0303", new Guid( "FA70E27D-6642-4162-AF17-530F66B507E7" )),
            ("f0304", new Guid( "FA70E27D-6642-4162-AF17-530F66B507E7" )),
            ("f0305", new Guid( "FA70E27D-6642-4162-AF17-530F66B507E7" )),
            ("f0401", new Guid( "C171D01E-C607-488B-A550-1E341081210B" )),
            ("f0402", new Guid( "C171D01E-C607-488B-A550-1E341081210B" )),
            ("f0403", new Guid( "C171D01E-C607-488B-A550-1E341081210B" )),
            ("f0404", new Guid( "C171D01E-C607-488B-A550-1E341081210B" )),
            ("f0405", new Guid( "C171D01E-C607-488B-A550-1E341081210B" )),
            ("f0501", new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" )),
            ("f0502", new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" )),
            ("f0503", new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" )),
            ("f0504", new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" )),
            ("f0505", new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" )),
            ("f0506", new Guid( "5635E95B-3A07-43B7-837A-0F131EF1DA97" )),
            ("f0601", new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" )),
            ("f0602", new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" )),
            ("f0603", new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" )),
            ("f0604", new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" )),
            ("f0605", new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" )),
            ("f0606", new Guid( "BD5D99E7-E0FF-4535-8B26-BF73EF9B9F89" )),
            ("f0701", new Guid( "73087DD2-B892-4367-894F-8922477B2F10" )),
            ("f0702", new Guid( "73087DD2-B892-4367-894F-8922477B2F10" )),
            ("f0703", new Guid( "73087DD2-B892-4367-894F-8922477B2F10" )),
            ("f0704", new Guid( "73087DD2-B892-4367-894F-8922477B2F10" )),
            ("f0705", new Guid( "73087DD2-B892-4367-894F-8922477B2F10" )),
            ("f0801", new Guid( "D84E58E4-87FC-4CEB-B83E-A2C6D186366C" )),
            ("f0802", new Guid( "D84E58E4-87FC-4CEB-B83E-A2C6D186366C" )),
            ("f0803", new Guid( "D84E58E4-87FC-4CEB-B83E-A2C6D186366C" )),
            ("f0804", new Guid( "D84E58E4-87FC-4CEB-B83E-A2C6D186366C" )),
            ("f0805", new Guid( "D84E58E4-87FC-4CEB-B83E-A2C6D186366C" )),
            ("f0901", new Guid( "6A2354C6-3FA4-4BAD-89A8-7359FEC48FE3" )),
            ("f0902", new Guid( "6A2354C6-3FA4-4BAD-89A8-7359FEC48FE3" )),
            ("f0903", new Guid( "6A2354C6-3FA4-4BAD-89A8-7359FEC48FE3" )),
            ("f0904", new Guid( "6A2354C6-3FA4-4BAD-89A8-7359FEC48FE3" )),
            ("f0905", new Guid( "6A2354C6-3FA4-4BAD-89A8-7359FEC48FE3" )),
            ("f1001", new Guid( "7EA44A56-58CB-4E40-9779-CC0A79772926" )),
            ("f1002", new Guid( "7EA44A56-58CB-4E40-9779-CC0A79772926" )),
            ("f1003", new Guid( "7EA44A56-58CB-4E40-9779-CC0A79772926" )),
            ("f1004", new Guid( "7EA44A56-58CB-4E40-9779-CC0A79772926" )),
            ("f1005", new Guid( "7EA44A56-58CB-4E40-9779-CC0A79772926" )),
            ("f1101", new Guid( "3F678404-5844-494F-BDB0-DD9FEEBC98C9" )),
            ("f1102", new Guid( "3F678404-5844-494F-BDB0-DD9FEEBC98C9" )),
            ("f1103", new Guid( "3F678404-5844-494F-BDB0-DD9FEEBC98C9" )),
            ("f1104", new Guid( "3F678404-5844-494F-BDB0-DD9FEEBC98C9" )),
            ("f1105", new Guid( "3F678404-5844-494F-BDB0-DD9FEEBC98C9" )),
            ("f1201", new Guid( "85459C0F-65A5-48F9-86F3-40B03F9C53E9" )),
            ("f1202", new Guid( "85459C0F-65A5-48F9-86F3-40B03F9C53E9" )),
            ("f1203", new Guid( "85459C0F-65A5-48F9-86F3-40B03F9C53E9" )),
            ("f1204", new Guid( "85459C0F-65A5-48F9-86F3-40B03F9C53E9" )),
            ("f1301", new Guid( "9F771853-2EBA-47A2-9AC5-26EBEA0A3B25" )),
            ("f1302", new Guid( "9F771853-2EBA-47A2-9AC5-26EBEA0A3B25" )),
            ("f1303", new Guid( "9F771853-2EBA-47A2-9AC5-26EBEA0A3B25" )),
            ("f1304", new Guid( "9F771853-2EBA-47A2-9AC5-26EBEA0A3B25" )),
            ("f1305", new Guid( "9F771853-2EBA-47A2-9AC5-26EBEA0A3B25" )),
            ("f1401", new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" )),
            ("f1402", new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" )),
            ("f1403", new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" )),
            ("f1404", new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" )),
            ("f1405", new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" )),
            ("f1406", new Guid( "4C898A5C-B48E-4BAE-AB89-835F25A451BF" )),
            ("f1501", new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" )),
            ("f1502", new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" )),
            ("f1503", new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" )),
            ("f1504", new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" )),
            ("f1505", new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" )),
            ("f1506", new Guid( "D7F9BDE2-8BEB-469E-BAD9-AA4DEBD3D995" )),
            ("f1601", new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" )),
            ("f1602", new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" )),
            ("f1603", new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" )),
            ("f1604", new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" )),
            ("f1605", new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" )),
            ("f1606", new Guid( "D8430EAD-7A38-4AD1-B21A-B2119EE0F1CD" )),
            ("F1701", new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" )),
            ("f1702", new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" )),
            ("f1703", new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" )),
            ("f1704", new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" )),
            ("f1705", new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" )),
            ("f1706", new Guid( "0D82DC77-334C-44B0-84A6-989910907DD4" )),
            ("f1801", new Guid( "2393C3CE-8E49-46FE-A75B-D5D624A37B49" )),
            ("f1802", new Guid( "2393C3CE-8E49-46FE-A75B-D5D624A37B49" )),
            ("f1803", new Guid( "2393C3CE-8E49-46FE-A75B-D5D624A37B49" )),
            ("f1804", new Guid( "2393C3CE-8E49-46FE-A75B-D5D624A37B49" )),
            ("f1805", new Guid( "2393C3CE-8E49-46FE-A75B-D5D624A37B49" )),
            ("f1901", new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" )),
            ("f1902", new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" )),
            ("f1903", new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" )),
            ("f1904", new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" )),
            ("f1905", new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" )),
            ("f1906", new Guid( "D7601B56-7495-4D7B-A916-8C48F78675E3" )),
            ("f2001", new Guid( "A027F6B2-56DD-4724-962D-F865606AEAB8" )),
            ("f2002", new Guid( "A027F6B2-56DD-4724-962D-F865606AEAB8" )),
            ("f2003", new Guid( "A027F6B2-56DD-4724-962D-F865606AEAB8" )),
            ("f2004", new Guid( "A027F6B2-56DD-4724-962D-F865606AEAB8" )),
            ("f2005", new Guid( "A027F6B2-56DD-4724-962D-F865606AEAB8" )),
            ("f2101", new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" )),
            ("f2102", new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" )),
            ("f2103", new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" )),
            ("f2104", new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" )),
            ("f2105", new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" )),
            ("f2106", new Guid( "4D0A1A6D-3F5A-476E-A633-04EAEF457645" )),
            ("f2201", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("f2202", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("f2203", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("f2204", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("f2205", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("f2206", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("f2207", new Guid( "EE1603BA-41AE-4CFA-B220-065768996501" )),
            ("ps0101", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" )),
            ("ps0102N", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" )),
            ("ps0103", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" )),
            ("ps0104N", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" )),
            ("ps0105", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" )),
            ("ps0109", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" )),
            ("ps0111", new Guid( "605F3702-6AE7-4545-BEBE-23693E60031C" ))
        };
    }
}
