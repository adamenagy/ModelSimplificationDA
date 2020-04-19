using System.Collections.Generic;
using Autodesk.Forge.DesignAutomation.Model;

namespace Interaction
{
    /// <summary>
    /// Customizable part of Publisher class.
    /// </summary>
    internal partial class Publisher
    {
        /// <summary>
        /// Constants.
        /// </summary>
        private static class Constants
        {
            private const int EngineVersion = 24;
            public static readonly string Engine = $"Autodesk.Inventor+{EngineVersion}";

            public const string Description = "Shrinkwraps model and converts to FBX";

            internal static class Bundle
            {
                public static readonly string Id = "ShrinkWrap";
                public const string Label = "prod";

                public static readonly AppBundle Definition = new AppBundle
                {
                    Engine = Engine,
                    Id = Id,
                    Description = Description
                };
            }

            internal static class Activity
            {
                public static readonly string Id = Bundle.Id;
                public const string Label = Bundle.Label;
            }

            internal static class Parameters
            {
                public const string inputFile = nameof(inputFile);
                public const string inputJson = nameof(inputJson);
                public const string outputIpt = nameof(outputIpt);
                public const string outputObjZip = nameof(outputObjZip);
                public const string outputFbx = nameof(outputFbx);
            }
        }


        /// <summary>
        /// Get command line for activity.
        /// </summary>
        private static List<string> GetActivityCommandLine()
        {
            return new List<string> {
              $"$(engine.path)\\InventorCoreConsole.exe /al $(appbundles[{Constants.Activity.Id}].path)",
              $"$(appbundles[ShrinkWrap].path)\\ShrinkWrapPlugin.bundle\\Contents\\converter.bat"
            };
        }

        /// <summary>
        /// Get activity parameters.
        /// </summary>
        private static Dictionary<string, Parameter> GetActivityParams()
        {
            return new Dictionary<string, Parameter>
                    {
                        {
                            Constants.Parameters.inputFile,
                            new Parameter
                            {
                                Verb = Verb.Get,
                                LocalName = "input",
                                Zip = true,
                                Description = "Input assembly to simplify"
                            }
                        },
                        {
                            Constants.Parameters.inputJson,
                            new Parameter
                            {
                                Verb = Verb.Get,
                                LocalName = "params.json",
                            }
                        },
                        {
                            Constants.Parameters.outputIpt,
                            new Parameter
                            {
                                Verb = Verb.Put,
                                LocalName = "output.ipt",
                            }
                        },
                        {
                            Constants.Parameters.outputObjZip,
                            new Parameter
                            {
                                Verb = Verb.Put,
                                Required = false,
                                Zip = true,
                                LocalName = "outputObjZip",
                            }
                        },
                        {
                            Constants.Parameters.outputFbx,
                            new Parameter
                            {
                                Verb = Verb.Put,
                                Required = false,
                                LocalName = "output.fbx",
                            }
                        }
                    };
        }

        /// <summary>
        /// Get arguments for workitem.
        /// </summary>
        private static Dictionary<string, IArgument> GetWorkItemArgs()
        {
            string json = System.IO.File.ReadAllText("params.json").Replace("\r\n", "");
            const string myOssBucket = "https://developer.api.autodesk.com/oss/v2/buckets/adam_simplify";
            const string accessToken = "eyJhbGciOiJIUzI1NiIsImtpZCI6Imp3dF9zeW1tZXRyaWNfa2V5In0.eyJzY29wZSI6WyJkYXRhOnJlYWQiLCJkYXRhOndyaXRlIiwiZGF0YTpjcmVhdGUiLCJidWNrZXQ6cmVhZCIsImJ1Y2tldDpkZWxldGUiLCJidWNrZXQ6dXBkYXRlIiwiYnVja2V0OmNyZWF0ZSJdLCJjbGllbnRfaWQiOiJyR20wbU85alZTc0QyeUJFRGs5TVJ0WFFUd3NhNjF5MCIsImF1ZCI6Imh0dHBzOi8vYXV0b2Rlc2suY29tL2F1ZC9qd3RleHA2MCIsImp0aSI6Imk0QjNpTGpTWmlqZVRtTXJPbzRKdmV6TFR0NldOUkV3RGxyMTRYR3pUdlp6UEtVWVdIbnQwUE9QejBnU0NqdHIiLCJleHAiOjE1ODczMTc1NzV9.EqfQbtCNWzw_spYroB3GfVG4LP4WZmZo1UGM6WiAQS0";

            // TODO: update the URLs below with real values
            return new Dictionary<string, IArgument>
                    {
                        {
                            Constants.Parameters.inputFile,
                            new XrefTreeArgument
                            {
                                Url = $"{myOssBucket}/objects/Engine MKII.iam.zip",
                                Headers = new Dictionary<string, string>()
                                {
                                    { "Authorization", "Bearer " + accessToken }
                                }
                            }
                        },
                        {
                            Constants.Parameters.inputJson,
                            new XrefTreeArgument
                            {
                                Url = "data:application/json," + json
                            }
                        },
                        {
                            Constants.Parameters.outputIpt,
                            new XrefTreeArgument
                            {
                                Verb = Verb.Put,
                                Url = $"{myOssBucket}/objects/output.ipt",
                                Headers = new Dictionary<string, string>()
                                {
                                    { "Authorization", "Bearer " + accessToken }
                                }
                            }
                        },
                        {
                            Constants.Parameters.outputObjZip,
                            new XrefTreeArgument
                            {
                                Verb = Verb.Put,
                                Url = $"{myOssBucket}/objects/output.obj.zip",
                                Headers = new Dictionary<string, string>()
                                {
                                    { "Authorization", "Bearer " + accessToken }
                                }
                            }
                        },
                        {
                            Constants.Parameters.outputFbx,
                            new XrefTreeArgument
                            {
                                Verb = Verb.Put,
                                 Url = $"{myOssBucket}/objects/output.fbx",
                                 Headers = new Dictionary<string, string>()
                                {
                                    { "Authorization", "Bearer " + accessToken }
                                }
                            }
                        }
                    };
        }
    }
}
