/////////////////////////////////////////////////////////////////////
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

const controlPrefix = "control_"

$(document).ready(function () {
    $('#clearAccount').click(clearAccount);
    $('#defineActivityShow').click(defineActivityModal);
    $('#createAppBundleActivity').click(createAppBundleActivity);
    $('#startWorkitem').click(startWorkitem);
    $('#inputFile').change(uploadFile);
    $('#uploadFile').click(() => {
        $('#inputFile').trigger('click')
    });
    $('#translateFile').click(translateFile);

    showConfigureButton();

    showDefaultModel();

    startConnection();

    showOptions("#optionsContainer", false);
});

function showDefaultModel() {
    jQuery.ajax({
        url: 'api/forge/designautomation/defaultmodel',
        method: 'GET',
        success: function (data) {
            launchViewer(data.urn, "forgeViewer1", 1)
        }
    });    
}

function uploadFile() {
    writeLog("Getting signed URL for upload")
    $.ajax({
        url: 'api/forge/designautomation/uploadurl?id=' + connectionId,
        success: function (res) {
            let signedUrl = res.signedUrl.data.signedUrl
            var inputFileField = document.getElementById('inputFile');
            var file = inputFileField.files[0];
            writeLog(`Uploading input file to signed URL ${signedUrl} ...`);
            $.ajax({
                url: signedUrl,
                data: file,
                processData: false,
                contentType: false,
                type: 'PUT',
                success: function (res) {
                    writeLog('File uploaded');
                    $('#fileName').text(file.name)
                    showOptions("#fileOptionsContainer", true)
                    $('#inputFile').html('')
                },
                error: function (err, err2) {
                    writeLog('File upload failed');
                    console.log(err)
                    $('#inputFile').html('')
                }
            });
        },
        error: function (err, err2) {
            writeLog('Could niot get signed URL');
            console.log(err)
            $('#inputFile').html('')
        }
    });
}

function translateFile() {
    showProgressIcon(1, true)

    let data = {
        browerConnectionId: connectionId,
        rootFilename: $("#control_MainAssembly").val(),
        isSimplified: false
    }

    jQuery.ajax({
        url: 'api/forge/designautomation/translations',
        method: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(data),
        success: function (res) {
            writeLog('Translating file... ');
        },
        error: function (res) {
            writeLog('Failed to start translation');
        }
    });    
}


function showOptions(controlId, isFileOptions) {
    let optionsContainer = $(controlId)
    optionsContainer.html('')
    optionsContainer.css('display', 'block')

    $.getJSON('params.json', function(params) {
        $.each(params, function(param, data) {
            if  (isFileOptions) {
                switch (data.type) {
                    case 'text':
                        optionsContainer.append(createTextControl(param, data, false))
                        break;
                }
            } else {
                switch (data.type) {
                    case 'boolean':
                        optionsContainer.append(createCheckboxControl(param, data))
                        break;
    
                    case 'enum':
                        optionsContainer.append(createListControl(param, data))
                        break;
    
                    case 'number':
                        optionsContainer.append(createTextControl(param, data, true))
                        break;
                }  
            }
      });
    });
}

function getOptions() {
    let controls = $(`[id^="${controlPrefix}"]`)
    let options = {}
    for (let key = 0; key <  controls.length; key++) {
        let control = controls[key]

        var val = null
        switch (control.type) {
            case "checkbox":
                val = control.checked
                break
            case "text":
                val = parseFloat(control.value)
                if (isNaN(val))
                    val = control.value
                break
            case "select-one":
                val = control.value
                break
        }
        
        options[control.id.replace(controlPrefix, "")] = {
            value: val
        }
    }

    return options
}

function getDisplayName(name, shift) {
    let words = name.match(/[A-Z][a-z]+/g);
    if (shift)
        words.shift()
    return words.join(" ")
}

function createListControl(name, data) {
    let id = controlPrefix + name 

    let text = ""
    for (let key in data.options) {
        let option = data.options[key]
        text += `<option value=${option}>${getDisplayName(option, true)}</option>`
    }

    text = 
    `<div class="form-group">
        <label for="${id}">${getDisplayName(name)}:</label>
        <select type="number" class="form-control" id="${id}">
            ${text}
        </select>
    </div>`

    return $(text)
}

function createCheckboxControl(name, data) {
    let id = controlPrefix + name 

    let text = 
    `<div class="form-group">
        <div class="form-check">
            <input class="form-check-input" type="checkbox" id="${id}" required>
            <label class="form-check-label" for="${id}">
                ${getDisplayName(name)}
            </label>
        </div>
    </div>`

    return $(text)
}

function createTextControl(name, data, isNumber) {
    let id = controlPrefix + name 

    let ph = (isNumber) ? "" : "e.g. " + data.placeholder
    let val = (isNumber) ? data.value : ""
    let suffix = (isNumber) ? " (cm)" : ""

    let text = 
    `<div class="form-group">
        <label for="${id}">
            ${getDisplayName(name) + suffix}
        </label>
        <input class="form-control" type="text" id="${id}" value="${val}" placeholder="${ph}" required>
    </div>`

    return $(text)
}

function showConfigureButton() {
    jQuery.ajax({
        url: 'api/forge/showconfigurebutton',
        method: 'GET',
        success: function (val) {
            if (val) {
                $("#showConfigureDialog").css("display", "block")
            }
        }
    });    
}

function clearAccount() {
    if (!confirm('Clear existing activities & appbundles before start. ' +
        'This is useful if you believe there are wrong settings on your account.' +
        '\n\nYou cannot undo this operation. Proceed?')) return;

    jQuery.ajax({
        url: 'api/forge/designautomation/account',
        method: 'DELETE',
        success: function () {
            prepareLists();
            writeLog('Account cleared, all appbundles & activities deleted');
        }
    });
}

function defineActivityModal() {
    $("#defineActivityModal").modal();
}

function createAppBundleActivity() {
    startConnection(function () {
        writeLog("Uploading sample files");
        uploadSourceFiles()
        writeLog("Defining appbundle and activity for " + $('#engines').val());
        $("#defineActivityModal").modal('toggle');
        createAppBundle(function () {
            createActivity()
        });
    });
}

function createAppBundle(cb) {
    jQuery.ajax({
        url: 'api/forge/designautomation/appbundles',
        method: 'POST',
        contentType: 'application/json',
        data: "{}",
        success: function (res) {
            writeLog('AppBundle: ' + res.appBundle + ', v' + res.version);
            if (cb) cb();
        }
    });
}

function createActivity(cb) {
    jQuery.ajax({
        url: 'api/forge/designautomation/activities',
        method: 'POST',
        contentType: 'application/json',
        data: "{}",
        success: function (res) {
            writeLog('Activity: ' + res.activity);
            if (cb) cb();
        }
    });
}

function uploadSourceFiles(cb) {
    jQuery.ajax({
        url: 'api/forge/designautomation/files',
        method: 'POST',
        contentType: 'application/json',
        data: "{}",
        success: function (res) {
            writeLog('File upload: done');
        }
    });
}

function showProgressIcon(number, show) {
   $("#progressIcon" + number).css('display', (show) ? 'block' : 'none')
   $("#startWorkitem").prop('disabled', show)
}

function startWorkitem() {
    showProgressIcon(2, true)

    let data = {
        browerConnectionId: connectionId,
        isDefault: $("#fileName").text() == "default",
        options: getOptions()
    };
    writeLog(data);
    startConnection(function () {
        writeLog('Starting workitem...');
        $.ajax({
            url: 'api/forge/designautomation/workitems',
            contentType: 'application/json',
            data: JSON.stringify(data),
            type: 'POST',
            success: function (res) {
                writeLog(`Workitem started: ${res.zipWorkItemId}`);
            }
        });
    });
}

function writeLog(text) {
  $('#outputlog').append('<div style="border-top: 1px dashed #C0C0C0">' + text + '</div>');
  var elem = document.getElementById('outputlog');
  elem.scrollTop = elem.scrollHeight;
}

var connection;
var connectionId;

function startConnection(onReady) {
    console.log("startConnection, connection = ", connection)
    if (connection && connection.connectionState) { if (onReady) onReady(); return; }
    connection = new signalR.HubConnectionBuilder().withUrl("/api/signalr/designautomation").build();
    connection.start()
        .then(function () {
            connection.invoke('getConnectionId')
                .then(function (id) {
                    console.log("getConnectionId, id = " + id);
                    connectionId = id; // we'll need this...
                    $("#startWorkitem").removeClass("disabled")
                    if (onReady) onReady();
                });
        });

    connection.on("onComplete", function (message) {
        writeLog(message);
        let data = {
            browerConnectionId: connectionId,
            rootFilename: $("#control_MainAssembly").val(),
            isSimplified: true
        }
        $.ajax({
            url: 'api/forge/designautomation/translations',
            contentType: 'application/json',
            data: JSON.stringify(data),
            type: 'POST',
            success: function () {
                writeLog(`Started translating simplified model`);
            }
        });
    });

    connection.on("onReport", function (message) {
        writeLog(message);
    });

    connection.on("onTranslated", async function (message) {
        writeLog(message);
        let data = JSON.parse(message)
        let viewerNumber = (data.isSimplified) ? 2 : 1
        showProgressIcon(viewerNumber, false)

        if (data.status === "success") {
            let elementId = "forgeViewer" + viewerNumber
            
            launchViewer(data.urn, elementId, viewerNumber)
        }
    });
}