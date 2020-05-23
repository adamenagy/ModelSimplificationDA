var viewers = [];
var counter = 0;

function launchViewer(urn, elementId, id) {
    // To avoid showing previous versions of the model from the cache
    Autodesk.Viewing.endpoint.HTTP_REQUEST_HEADERS['If-Modified-Since'] = "Sat, 29 Oct 1994 19:43:31 GMT"

    return new Promise(async (resolve, reject) => {
        var options = {
            env: 'AutodeskProduction',
            getAccessToken: getForgeToken
        };

        if (viewers[id]) {
            viewers[id].tearDown();
            viewers[id].setUp(viewers[id].config);

            await loadModels(urn, id)

            resolve()
        } else {
            Autodesk.Viewing.Initializer(options, async () => {
                viewers[id] = new Autodesk.Viewing.GuiViewer3D(document.getElementById(elementId), { extensions: ['Autodesk.DocumentBrowser'] });
                viewers[id].start();
                await loadModels(urn, id)

                resolve()
            });
        }
    })
}

function loadModels(urn, id) {
    return new Promise(async (resolve, reject) => {       
        console.log('loadModels()');

        var documentId = 'urn:' + urn;

        console.log('before promise')
        await loadModel(documentId, id)
        console.log('after promise')

        resolve()
    })
}

function loadModel(documentId, id) {
    return new Promise((resolve, reject) => {
        let onDocumentLoadSuccess = (doc) => {
            console.log(`onDocumentLoadSuccess() - counter = ${counter}`);
            var viewables = doc.getRoot().getDefaultGeometry();
            viewers[id].loadDocumentNode(doc, viewables, {}).then(i => {
                resolve()
            });
        }
        
        let onDocumentLoadFailure = (viewerErrorCode) => {
            console.error('onDocumentLoadFailure() - errorCode:' + viewerErrorCode);
            reject()
        }

        Autodesk.Viewing.Document.load(documentId, onDocumentLoadSuccess, onDocumentLoadFailure);
    })
}

function getForgeToken(callback) {
    fetch('/api/forge/oauth/token').then(res => {
        res.json().then(data => {
            callback(data.access_token, data.expires_in);
        });
    });
}
