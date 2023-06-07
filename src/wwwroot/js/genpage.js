let gen_param_types = null;

let session_id = null;

let batches = 0;

let lastImageDir = '';

let lastModelDir = '';

const time_started = Date.now();

function clickImageInBatch(div) {
    setCurrentImage(div.getElementsByTagName('img')[0].src);
}

function selectImageInHistory(div) {
    let batchId = div.dataset.batch_id;
    document.getElementById('current_image_batch').innerHTML = '';
    for (let img of document.getElementById('image_history').querySelectorAll(`[data-batch_id="${batchId}"]`)) {
        let batch_div = appendImage('current_image_batch', img.getElementsByTagName('img')[0].src, batchId, '(TODO)');
        batch_div.addEventListener('click', () => clickImageInBatch(batch_div));
    }
    setCurrentImage(div.getElementsByTagName('img')[0].src);
}

function setCurrentImage(src) {
    let curImg = document.getElementById('current_image');
    curImg.innerHTML = '';
    let img = document.createElement('img');
    img.src = src;
    curImg.appendChild(img);
}

function appendImage(container, imageSrc, batchId, textPreview) {
    if (typeof container == 'string') {
        container = document.getElementById(container);
    }
    let div = createDiv(null, `image-block image-batch-${batchId % 2}`);
    div.dataset.batch_id = batchId;
    let img = document.createElement('img');
    img.addEventListener('load', () => {
        let ratio = img.width / img.height;
        div.style.width = `${(ratio * 8) + 2}rem`;
    });
    img.src = imageSrc;
    div.appendChild(img);
    let textBlock = createDiv(null, 'image-preview-text');
    textBlock.innerText = textPreview;
    div.appendChild(textBlock);
    container.appendChild(div);
    return div;
}

function gotImageResult(image) {
    let src = image;
    let batch_div = appendImage('current_image_batch', src, batches, '(TODO)');
    batch_div.addEventListener('click', () => clickImageInBatch(batch_div));
    let history_div = appendImage('image_history', src, batches, '(TODO)');
    history_div.addEventListener('click', () => selectImageInHistory(history_div));
    setCurrentImage(src);
}

function getGenInput() {
    let input = {};
    for (let type of gen_param_types) {
        if (type.toggleable && !document.getElementById(`input_${type.id}_toggle`).checked) {
            continue;
        }
        let elem = document.getElementById('input_' + type.id);
        if (type.type == "boolean") {
            input[type.id] = elem.checked;
        }
        else {
            input[type.id] = elem.value;
        }
    }
    console.log("Will request: " + JSON.stringify(input));
    return input;
}

function doGenerate() {
    if (session_id == null) {
        if (Date.now() - time_started > 1000 * 60) {
            showError("Cannot generate, session not started. Did the server crash?");
        }
        else {
            showError("Cannot generate, session not started. Please wait a moment for the page to load.");
        }
        return;
    }
    setCurrentModel(() => {
        if (document.getElementById('current_model').innerText == '') {
            showError("Cannot generate, no model selected.");
            return;
        }
        document.getElementById('current_image_batch').innerHTML = '';
        batches++;
        makeWSRequest('GenerateText2ImageWS', getGenInput(), data => {
            gotImageResult(data.image);
        });
    });
}

class FileListCallHelper {
    // Attempt to prevent callback recursion.
    // In practice this seems to not work.
    // Either JavaScript itself or Firefox seems to really love tracking the stack and refusing to let go.
    // TODO: Guarantee it actually works so we can't stack overflow from file browsing ffs.
    constructor(path, loadCaller) {
        this.path = path;
        this.loadCaller = loadCaller;
    }
    call() {
        this.loadCaller(this.path);
    }
};

function loadFileList(api, path, container, loadCaller, fileCallback, endCallback) {
    genericRequest(api, {'path': path}, data => {
        let prefix;
        if (path == '') {
            prefix = '';
        }
        else {
            prefix = path + '/';
            let above = path.split('/').slice(0, -1).join('/');
            let div = appendImage(container, '/imgs/folder_up.png', 'folder', `../`);
            let helper = new FileListCallHelper(above, loadCaller);
            div.addEventListener('click', helper.call.bind(helper));
        }
        for (let folder of data.folders.sort()) {
            let div = appendImage(container, '/imgs/folder.png', 'folder', `${folder}/`);
            let helper = new FileListCallHelper(`${prefix}${folder}`, loadCaller);
            div.addEventListener('click', helper.call.bind(helper));
        }
        container.appendChild(document.createElement('br'));
        for (let file of data.files.sort()) {
            fileCallback(prefix, file);
        }
        if (endCallback) {
            endCallback();
        }
    });
}

function loadHistory(path) {
    let container = document.getElementById('image_history');
    lastImageDir = path;
    container.innerHTML = '';
    loadFileList('ListImages', path, container, loadHistory, (prefix, img) => {
        let div = appendImage('image_history', `Output/${prefix}${img.src}`, img.batch_id, img.src);
        div.addEventListener('click', () => selectImageInHistory(div));
    });
}

let models = {};
let cur_model = null;

function appendModel(container, prefix, model) {
    models[`${prefix}${model.name}`] = model;
    let batch = document.getElementById('current_model').innerText == model.name ? 'model-selected' : (model.loaded ? 'model-loaded' : `image-batch-${Object.keys(models).length % 2}`);
    let div = createDiv(null, `model-block model-block-hoverable ${batch}`);
    let img = document.createElement('img');
    img.src = model.preview_image;
    div.appendChild(img);
    let textBlock = createDiv(null, 'model-descblock');
    textBlock.innerText = `${model.name}\n${model.description}\n`;
    let loadButton = createDiv(null, 'model-load-button');
    loadButton.innerText = 'Load Now';
    textBlock.appendChild(loadButton);
    div.appendChild(textBlock);
    container.appendChild(div);
    div.addEventListener('click', () => {
        document.getElementById('input_model').value = model.name;
        document.getElementById('current_model').innerText = model.name;
        loadModelList(lastModelDir);
    });
    loadButton.addEventListener('click', () => {
        document.getElementById('input_model').value = model.name;
        document.getElementById('current_model').innerText = model.name;
        for (let possible of container.getElementsByTagName('div')) {
            possible.classList.remove('model-block-hoverable');
            possible.parentElement.replaceChild(possible.cloneNode(true), possible);
        }
        genericRequest('SelectModel', {'model': model.name}, data => {
            loadModelList(lastModelDir);
        });
    });
}

function loadModelList(path) {
    let container = document.getElementById('model_list');
    lastModelDir = path;
    container.innerHTML = '';
    models = {};
    loadFileList('ListModels', path, container, loadModelList, (prefix, model) => {
        appendModel(container, prefix, model);
    }, () => {
        let current_model = document.getElementById('current_model');
        if (current_model.innerText == '') {
            let model = Object.values(models).find(m => m.loaded);
            if (model) {
                document.getElementById('input_model').value = model.name;
                current_model.innerText = model.name;
            }
        }
    });
}

function toggle_advanced() {
    let advancedArea = document.getElementById('main_inputs_area_advanced');
    let toggler = document.getElementById('advanced_options_checkbox');
    advancedArea.style.display = toggler.checked ? 'block' : 'none';
}
function toggle_advanced_checkbox_manual() {
    let toggler = document.getElementById('advanced_options_checkbox');
    toggler.checked = !toggler.checked;
    toggle_advanced();
}

let statusBarElem = document.getElementById('top_status_bar');

function getCurrentStatus() {
    if (!hasLoadedBackends) {
        return ['warn', 'Loading...'];
    }
    if (Object.values(backends_loaded).length == 0) {
        return ['warn', 'No backends present. You must configure backends on the Settings page before you can continue.'];
    }
    let loading = countBackendsByStatus('waiting') + countBackendsByStatus('loading');
    if (countBackendsByStatus('running') == 0) {
        if (loading > 0) {
            return ['warn', 'Backends are still loading on the server...'];
        }
        if (countBackendsByStatus('errored') > 0) {
            return ['error', 'Some backends have errored on the server. Check the server logs for details.'];
        }
        if (countBackendsByStatus('disabled') > 0) {
            return ['warn', 'Some backends are disabled. Please configure them to continue.'];
        }
        return ['error', 'Something is wrong with your backends. Please check the Backends Settings page or the server logs.'];
    }
    if (loading > 0) {
        return ['soft', 'Some backends are ready, but others are still loading...'];
    }
    return ['', ''];
}

function reviseStatusBar() {
    let status = getCurrentStatus();
    statusBarElem.innerText = status[1];
    statusBarElem.className = `top-status-bar status-bar-${status[0]}`;
}

function genpageLoop() {
    backendLoopUpdate();
    reviseStatusBar();
}

let mouseX, mouseY;
let popHide = [];

document.addEventListener('click', (e) => {
    mouseX = e.pageX;
    mouseY = e.pageY;
    for (let id of popHide) {
        let pop = document.getElementById(`popover_${id}`);
        pop.style.display = 'none';
        pop.dataset.visible = "false";
    }
    popHide = [];
}, true);

function doPopover(id) {
    let pop = document.getElementById(`popover_${id}`);
    if (pop.dataset.visible == "true") {
        pop.style.display = 'none';
        pop.dataset.visible = "false";
        delete popHide[popHide.indexOf(id)]; // wtf? JavaScript doesn't have remove(...)?
    }
    else {
        pop.style.display = 'block';
        pop.dataset.visible = "true";
        pop.style.left = `${mouseX}px`;
        pop.style.top = `${mouseY}px`;
        popHide.push(id);
    }
}

function genInputs() {
    let area = document.getElementById('main_inputs_area');
    let advancedAea = document.getElementById('main_inputs_area_advanced');
    let hiddenArea = document.getElementById('main_inputs_area_hidden');
    let html = '', advancedHtml = '', hiddenHtml = '';
    for (let param of gen_param_types) {
        let paramHtml;
        // Actual HTML popovers are too new at time this code was written (experimental status, not supported on most browsers)
        let example = param.examples ? `<br><br>Examples: <code>${param.examples.map(escapeHtml).join("</code>,&emsp;<code>")}</code>` : '';
        let pop = `<div class="sui-popover" id="popover_input_${param.id}"><b>${escapeHtml(param.name)}</b> (${param.type}):<br>&emsp;${escapeHtml(param.description)}${example}</div>`;
        switch (param.type) {
            case 'text':
                paramHtml = makeTextInput(param.feature_flag, `input_${param.id}`, param.name, '', param.default, 3, param.description, param.toggleable, pop);
                break;
            case 'decimal':
            case 'integer':
                let min = param.min;
                let max = param.max;
                if (min == 0 && max == 0) {
                    min = -9999999;
                    max = 9999999;
                }
                paramHtml = makeNumberInput(param.feature_flag, `input_${param.id}`, param.name, param.description, param.default, param.min, param.max, param.step, true, param.toggleable, pop);
                break;
            case 'pot_slider':
                paramHtml = makeSliderInput(param.feature_flag, `input_${param.id}`, param.name, param.description, param.default, param.min, param.max, param.step, true, param.toggleable, pop);
                break;
            case 'boolean':
                paramHtml = makeCheckboxInput(param.feature_flag, `input_${param.id}`, param.name, param.description, param.default, param.toggleable, pop);
                break;
            case 'dropdown':
                paramHtml = makeDropdownInput(param.feature_flag, `input_${param.id}`, param.name, param.description, param.values, param.default, param.toggleable, pop);
                break;
        }
        paramHtml += pop;
        if (!param.visible) {
            hiddenHtml += paramHtml;
        }
        else if (param.advanced) {
            advancedHtml += paramHtml;
        }
        else {
            html += paramHtml;
        }
    }
    area.innerHTML = html;
    advancedAea.innerHTML = advancedHtml;
    hiddenArea.innerHTML = hiddenHtml;
    enableSlidersIn(area);
    for (let param of gen_param_types) {
        if (param.toggleable) {
            doToggleEnable(`input_${param.id}`);
        }
    }
}

let toolSelector = document.getElementById('tool_selector');
let toolContainer = document.getElementById('tool_container');

function genToolsList() {
    toolSelector.value = '';
    // TODO: Dynamic-from-server option list generation
    toolSelector.addEventListener('change', () => {
        for (let opened of toolContainer.getElementsByClassName('tool-open')) {
            opened.classList.remove('tool-open');
        }
        let tool = toolSelector.value;
        if (tool == '') {
            return;
        }
        let div = document.getElementById(`tool_${tool}`);
        div.classList.add('tool-open');
    });
}

function registerNewTool(id, name) {
    let option = document.createElement('option');
    option.value = id;
    option.innerText = name;
    toolSelector.appendChild(option);
    let div = createDiv(`tool_${id}`, 'tool');
    toolContainer.appendChild(div);
    return div;
}

let sessionReadyCallbacks = [];

function setCurrentModel(callback) {
    let currentModel = document.getElementById('current_model');
    if (currentModel.innerText == '') {
        genericRequest('ListLoadedModels', {}, data => {
            if (data.models.length > 0) {
                currentModel.innerText = data.models[0].name;
                document.getElementById('input_model').value = data.models[0].name;
            }
            if (callback) {
                callback();
            }
        });
    }
    else {
        if (callback) {
            callback();
        }
    }
}

function pageSizer() {
    let topSplit = document.getElementById('t2i-top-split-bar');
    let midSplit = document.getElementById('t2i-mid-split-bar');
    let topBar = document.getElementById('t2i_top_bar');
    let bottomBarContent = document.getElementById('t2i_bottom_bar_content');
    let inputSidebar = document.getElementById('input_sidebar');
    let mainInputsAreaWrapper = document.getElementById('main_inputs_area_wrapper');
    let mainImageArea = document.getElementById('main_image_area');
    let currentImageBatch = document.getElementById('current_image_batch');
    let topDrag = false;
    let midDrag = false;
    topSplit.addEventListener('mousedown', (e) => {
        topDrag = true;
        e.preventDefault();
    }, true);
    midSplit.addEventListener('mousedown', (e) => {
        midDrag = true;
        e.preventDefault();
    }, true);
    document.addEventListener('mousemove', (e) => {
        if (topDrag) {
            let offX = e.pageX - 2;
            inputSidebar.style.width = `${offX}px`;
            mainInputsAreaWrapper.style.width = `${offX}px`;
            mainImageArea.style.width = `calc(100vw - ${offX}px)`;
            currentImageBatch.style.width = `calc(100vw - ${offX}px - min(max(40vw, 28rem), 49vh))`;
        }
        if (midDrag) {
            let topY = currentImageBatch.getBoundingClientRect().top;
            let offY = (e.pageY - topY - 2) / window.innerHeight * 100;
            topSplit.style.height = `${offY}vh`;
            inputSidebar.style.height = `${offY}vh`;
            mainInputsAreaWrapper.style.height = `calc(${offY}vh - 6rem)`;
            mainImageArea.style.height = `${offY}vh`;
            topBar.style.height = `${offY}vh`;
            let invOff = 100 - offY;
            bottomBarContent.style.height = `calc(${invOff}vh - 2rem)`;
        }
    });
    document.addEventListener('mouseup', (e) => {
        topDrag = false;
        midDrag = false;
    });
}

function genpageLoad() {
    console.log('Load page...');
    pageSizer();
    reviseStatusBar();
    getSession(() => {
        console.log('First session loaded - prepping page.');
        loadHistory('');
        loadModelList('');
        loadBackendTypesMenu();
        genericRequest('ListT2IParams', {}, data => {
            gen_param_types = data.list.sort((a, b) => a.priority - b.priority);
            genInputs(data);
            genToolsList();
            reviseStatusBar();
            toggle_advanced();
            setCurrentModel();
            document.getElementById('generate_button').addEventListener('click', doGenerate);
            document.getElementById('image_history_refresh_button').addEventListener('click', () => loadHistory(lastImageDir));
            document.getElementById('model_list_refresh_button').addEventListener('click', () => loadModelList(lastModelDir));
            for (let callback of sessionReadyCallbacks) {
                callback();
            }
        });
    });
    setInterval(genpageLoop, 1000);
}

setTimeout(genpageLoad, 1);
