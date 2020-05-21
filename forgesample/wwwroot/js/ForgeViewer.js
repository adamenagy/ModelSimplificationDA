var viewer;
var counter = 0;

function launchViewer(urn, elementId) {
    // To avoid showing previous versions of the model from the cache
    Autodesk.Viewing.endpoint.HTTP_REQUEST_HEADERS['If-Modified-Since'] = "Sat, 29 Oct 1994 19:43:31 GMT"

    return new Promise(async (resolve, reject) => {
        var options = {
            env: 'AutodeskProduction',
            getAccessToken: getForgeToken
        };

        if (viewer) {
            viewer.tearDown();
            viewer.setUp(viewer.config);

            await loadModels(urn)

            resolve()
        } else {
            Autodesk.Viewing.Initializer(options, async () => {
                viewer = new Autodesk.Viewing.GuiViewer3D(document.getElementById(elementId), { extensions: ['Autodesk.DocumentBrowser'] });
                viewer.start();
                await loadModels(urn)

                resolve()
            });
        }
    })
}

function loadModels(urn) {
    return new Promise(async (resolve, reject) => {       
        console.log('loadModels()');

        var documentId = 'urn:' + urn;

        console.log('before promise')
        await loadModel(documentId)
        console.log('after promise')

        resolve()
    })
}

function loadModel(documentId) {
    return new Promise((resolve, reject) => {
        let onDocumentLoadSuccess = (doc) => {
            console.log(`onDocumentLoadSuccess() - counter = ${counter}`);
            var viewables = doc.getRoot().getDefaultGeometry();
            viewer.loadDocumentNode(doc, viewables, {}).then(i => {
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
