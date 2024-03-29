﻿/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Inventor;
using Autodesk.Forge.DesignAutomation.Inventor.Utils;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace ShrinkWrapPlugin
{
    [ComVisible(true)]
    public class SampleAutomation
    {
        private readonly InventorServer inventorApplication;

        public SampleAutomation(InventorServer inventorApp)
        {
            inventorApplication = inventorApp;
        }

        public Dictionary<string, dynamic> CreateEnumMap()
        {
            Type[] enumTypes = new Type[]
            {
                typeof(DerivedComponentStyleEnum),
                typeof(ShrinkwrapRemoveStyleEnum)
            };

            var map = new Dictionary<string, dynamic>();
            foreach (var enumType in enumTypes)
            {
                foreach (var value in Enum.GetValues(enumType))
                {
                    var name = Enum.GetName(enumType, value);
                    map.Add(name, value);
                }
            }

            return map;
        }

        private T GetValue<T>(JObject obj, string name)
        {
            try
            {
                JObject val = obj.GetValue(name) as JObject;
                return val.GetValue("value").Value<T>();
            }
            catch
            {
                return default;
            }
        }

        BIMComponent getBIMComponent(Document doc)
        {
            BIMComponent bimComponent = null;
            var docType = doc.DocumentType;
            if (docType == DocumentTypeEnum.kAssemblyDocumentObject)
            {
                AssemblyDocument _doc = doc as AssemblyDocument;
                bimComponent = _doc.ComponentDefinition.BIMComponent;
            }
            else if (docType == DocumentTypeEnum.kPartDocumentObject)
            {
                PartDocument _doc = doc as PartDocument;
                bimComponent = _doc.ComponentDefinition.BIMComponent;
            }
            else
            {
                Trace.TraceInformation("NOT supported document type.");
            }

            return bimComponent;
        }

        public void ExportRFA(Document doc, string filePath)
        {
            LogTrace("Exporting RFA file.");

            BIMComponent bimComponent = getBIMComponent(doc);
            if (bimComponent == null)
            {
                LogTrace("Could not export RFA file.");
                return;
            }

            NameValueMap nvm = inventorApplication.TransientObjects.CreateNameValueMap();
            string currentDir = System.IO.Directory.GetCurrentDirectory();
            var reportFileName = System.IO.Path.Combine(currentDir, "Report.html");
            nvm.Add("ReportFileName", reportFileName);
            bimComponent.ExportBuildingComponentWithOptions(filePath, nvm);

            LogTrace("Exported RFA file.");
        }

        public void Run(Document doc)
        {
            LogTrace("Run()");

            string currentDir = System.IO.Directory.GetCurrentDirectory();
            LogTrace("Current Dir = " + currentDir);
            string filesDir = System.IO.Path.Combine(currentDir, "files");
            LogTrace("Files Dir = " + filesDir);

            var map = CreateEnumMap();

            JObject parameters = JObject.Parse(System.IO.File.ReadAllText("input.json"));

            string assemblyName = GetValue<string>(parameters, "MainAssembly");
            LogTrace($"MainAssembly = {assemblyName}");
            if (assemblyName == null)
            {
                LogTrace("MainAssembly not specified. Exiting.");
                return;
            }

            using (new HeartBeat())
            {
                string projectName = GetValue<string>(parameters, "ProjectFile");
                LogTrace($"ProjectFile = {projectName}");
                if (projectName != default && projectName != "")
                {
                    string [] projectFileNames = System.IO.Directory.GetFiles(filesDir, projectName, System.IO.SearchOption.AllDirectories);
                    if (projectFileNames.Length > 0)
                    {
                        string fullProjectPath = projectFileNames[0];
                        LogTrace("fullProjectPath = " + fullProjectPath);
                        LogTrace("Loading and activating project");
                        DesignProject dp = inventorApplication.DesignProjectManager.DesignProjects.AddExisting(fullProjectPath);
                        dp.Activate();
                    }
                    else
                    {
                        LogTrace("Project file not found");
                    }
                }

                string [] assemblyFileNames = System.IO.Directory.GetFiles(filesDir, assemblyName, System.IO.SearchOption.AllDirectories);
                if (assemblyFileNames.Length < 1)
                {
                    LogTrace("Did not find assembly");
                    return;
                }

                string fullAssemblyPath = assemblyFileNames[0];
                LogTrace("fullAssemblyPath = " + fullAssemblyPath);

                string LOD = GetValue<string>(parameters, "LevelOfDetail");
                LogTrace($"LevelOfDetail = {LOD}");
                if (LOD != default && LOD != "")
                {
                    string[] LODs = inventorApplication.FileManager.GetLevelOfDetailRepresentations(fullAssemblyPath);

                    if (LODs.Contains<string>(LOD))
                    {
                        fullAssemblyPath += "<" + LOD + ">";
                    }
                    else
                    {
                        LogTrace($"Assembly does not have a LevelOfDetail named {LOD}");
                        LogTrace("Available LODs:");
                        foreach (var item in LODs)
                        {
                            LogTrace(item);
                        }
                    }
                }
                LogTrace("fullAssemblyPath = " + fullAssemblyPath);
                AssemblyDocument asmDoc = inventorApplication.Documents.Open(fullAssemblyPath, true) as AssemblyDocument;
                
                LogTrace("Opened input assembly file");
                AssemblyComponentDefinition compDef = asmDoc.ComponentDefinition;

                LogTrace("Before Update2");
                asmDoc.Update2(true);
                LogTrace("After Update2");

                PartDocument partDoc = inventorApplication.Documents.Add(DocumentTypeEnum.kPartDocumentObject, "", true) as PartDocument;
                LogTrace("Created part document");
                PartComponentDefinition partCompDef = partDoc.ComponentDefinition;

                ShrinkwrapDefinition SWD = null;
                try
                {
                    LogTrace("asmDoc.FullDocumentName = " + asmDoc.FullDocumentName);
                    LogTrace("LOD = " + asmDoc.LevelOfDetailName);
                    SWD = partCompDef.ReferenceComponents.ShrinkwrapComponents.CreateDefinition(asmDoc.FullDocumentName);
                    LogTrace("After ShrinkwrapComponents.CreateDefinition");
                    if (parameters.ContainsKey("CreateIndependentBodiesOnFailedBoolean"))
                    {
                        SWD.CreateIndependentBodiesOnFailedBoolean =
                            GetValue<bool>(parameters, "CreateIndependentBodiesOnFailedBoolean");
                    }
                    if (parameters.ContainsKey("DeriveStyle"))
                    {
                        // e.g. DerivedComponentStyleEnum.kDeriveAsSingleBodyNoSeams
                        SWD.DeriveStyle = map[GetValue<string>(parameters, "DeriveStyle")];
                    }
                    if (parameters.ContainsKey("RemoveInternalParts"))
                    {
                        SWD.RemoveInternalParts = GetValue<bool>(parameters, "RemoveInternalParts");
                    }
                    if (parameters.ContainsKey("RemoveAllInternalVoids"))
                    {
                        SWD.RemoveAllInternalVoids = GetValue<bool>(parameters, "RemoveAllInternalVoids"); 
                    }
                    if (parameters.ContainsKey("RemoveHolesDiameterRange"))
                    {
                        SWD.RemoveHolesDiameterRange = GetValue<double>(parameters, "RemoveHolesDiameterRange"); // in cm
                    }
                    if (parameters.ContainsKey("RemoveHolesStyle"))
                    {
                        SWD.RemoveHolesStyle = map[GetValue<string>(parameters, "RemoveHolesStyle")];
                    }
                    if (parameters.ContainsKey("RemoveFilletsRadiusRange"))
                    {
                        SWD.RemoveFilletsRadiusRange = GetValue<double>(parameters, "RemoveFilletsRadiusRange"); // in cm
                    }
                    if (parameters.ContainsKey("RemoveFilletsStyle"))
                    {
                        SWD.RemoveFilletsStyle = map[GetValue<string>(parameters, "RemoveFilletsStyle")];
                    }
                    if (parameters.ContainsKey("RemoveChamfersDistanceRange"))
                    {
                        SWD.RemoveChamfersDistanceRange = GetValue<double>(parameters, "RemoveChamfersDistanceRange");
                    }
                    if (parameters.ContainsKey("RemoveChamfersStyle"))
                    {
                        SWD.RemoveChamfersStyle = map[GetValue<string>(parameters, "RemoveChamfersStyle")];
                    }
                    if (parameters.ContainsKey("RemovePartsBySize"))
                    {
                        SWD.RemovePartsBySize = GetValue<bool>(parameters, "RemovePartsBySize");
                    }
                    if (parameters.ContainsKey("RemovePartsSize"))
                    {
                        SWD.RemovePartsSize = GetValue<double>(parameters, "RemovePartsSize"); // in cm
                    }
                    if (parameters.ContainsKey("RemovePocketsStyle"))
                    {
                        SWD.RemovePocketsStyle = map[GetValue<string>(parameters, "RemovePocketsStyle")];
                    }
                    if (parameters.ContainsKey("RemovePocketsMaxFaceLoopRange"))
                    {
                        SWD.RemovePocketsMaxFaceLoopRange = GetValue<double>(parameters, "RemovePocketsMaxFaceLoopRange"); // in cm
                    }

                    LogTrace("Before ShrinkwrapComponents.Add");
                    ShrinkwrapComponent SWComp = null;
                    try
                    {
                        SWComp = partCompDef.ReferenceComponents.ShrinkwrapComponents.Add(SWD);
                    }
                    catch (Exception ex)
                    {
                        LogTrace(ex.Message);
                        SWComp = partCompDef.ReferenceComponents.ShrinkwrapComponents[1];
                    }
                    LogTrace("After ShrinkwrapComponents.Add");

                    LogTrace("Before SuppressLinkToFile");
                    try
                    {
                        if (parameters.ContainsKey("SuppressLinkToFile"))
                        { 
                            SWComp.SuppressLinkToFile = GetValue<bool>(parameters, "SuppressLinkToFile");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTrace(ex.Message);
                    }
                    LogTrace("After SuppressLinkToFile");

                    LogTrace("Saving part document");
                    partDoc.SaveAs(System.IO.Path.Combine(filesDir, "output.ipt"), false);
                    LogTrace("Saved part document to output.ipt");

                    bool createObj = GetValue<bool>(parameters, "CreateObj");
                    if (createObj == true)
                    {
                        LogTrace("Saving to OBJ");
                        partDoc.SaveAs(System.IO.Path.Combine(currentDir, "outputObjZip", "output.obj"), true);
                        LogTrace("Saved to OBJ named output.obj");
                    }

                    bool createRfa = GetValue<bool>(parameters, "CreateRfa");
                    if (createRfa == true)
                    {
                        LogTrace("Saving to RFA");
                        string rfaPath = System.IO.Path.Combine(currentDir, "output.rfa");
                        ExportRFA(partDoc as Document, rfaPath);
                        LogTrace("Saved to RFA named output.rfa");
                    }
                }
                catch (Exception ex)
                {
                    LogTrace("Error: " + ex.Message);
                }

                LogTrace("Finished");
            }
        }

        public void RunWithArguments(Document doc, NameValueMap map)
        {
            LogTrace("RunWithArguments()");

            Run(doc);
        }

        #region Logging utilities

        /// <summary>
        /// Log message with 'trace' log level.
        /// </summary>
        private static void LogTrace(string format, params object[] args)
        {
            Trace.TraceInformation(format, args);
        }

        /// <summary>
        /// Log message with 'trace' log level.
        /// </summary>
        private static void LogTrace(string message)
        {
            Trace.TraceInformation(message);
        }

        /// <summary>
        /// Log message with 'error' log level.
        /// </summary>
        private static void LogError(string format, params object[] args)
        {
            Trace.TraceError(format, args);
        }

        /// <summary>
        /// Log message with 'error' log level.
        /// </summary>
        private static void LogError(string message)
        {
            Trace.TraceError(message);
        }

        #endregion
    }
}