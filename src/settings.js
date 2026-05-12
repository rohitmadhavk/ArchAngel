console.log('[SETTINGS] Settings webview loaded');

const vscode = acquireVsCodeApi();

// DOM elements
const repoUrlInput = document.getElementById('repoUrl');
const repoBranchInput = document.getElementById('repoBranch');
const addRepoBtn = document.getElementById('addRepoBtn');
const refreshAllBtn = document.getElementById('refreshAllBtn');
const syncRepoBtn = document.getElementById('syncRepoBtn');
const repoContainer = document.getElementById('repoContainer');
const useVSCodeAuthBtn = document.getElementById('useVSCodeAuthBtn');
const checkAuthStatusBtn = document.getElementById('checkAuthStatusBtn');

let repositories = [];

// Event listeners
addRepoBtn?.addEventListener('click', addRepository);
refreshAllBtn?.addEventListener('click', refreshAllRepositories);
syncRepoBtn?.addEventListener('click',syncRepositories);
useVSCodeAuthBtn?.addEventListener('click',useVSCodeAuth);
checkAuthStatusBtn?.addEventListener('click',checkAuth);

document.querySelectorAll('.demo-repo').forEach(el => {
    el.addEventListener('click', () => {
        const owner = el.getAttribute('data-owner');
        const name = el.getAttribute('data-name');
        if (owner && name) addDemoRepo(owner, name);
    });
});

// Handle messages from extension
window.addEventListener('message', event => {
    const message = event.data;
    console.log('[SETTINGS] Received message:', message);
    
    switch (message.type) {
        case 'repositoriesUpdate':
            repositories = message.data || [];
            renderRepositories();
            break;
        case 'authStatus':
            console.log('[SETTINGS] Processing authStatus:', message.data);
            updateAuthStatus(message.data);
            break;
        default:
            console.log('[SETTINGS] Unknown message type:', message.type);
    }
});

// Request initial data
console.log('[SETTINGS] Requesting initial data...');
vscode.postMessage({ type: 'getRepositories' });
vscode.postMessage({ type: 'checkAuth' });

// Authentication functions
function useVSCodeAuth() {
    console.log('[SETTINGS] useVSCodeAuth called');
    vscode.postMessage({ type: 'authAction', action: 'use-vscode-auth' });
}

function setupToken() {
    console.log('[SETTINGS] setupToken called');
    vscode.postMessage({ type: 'authAction', action: 'setup-token' });
}

function checkAuth() {
    console.log('[SETTINGS] checkAuth called');
    vscode.postMessage({ type: 'checkAuth' });
}

// Demo functions
function addDemoRepo(owner, name) {
    console.log('[SETTINGS] addDemoRepo called:', owner, name);
    repoUrlInput.value = owner + '/' + name;
    addRepository();
}

function updateAuthStatus(status) {
    console.log('[SETTINGS] updateAuthStatus called with:', status);
    
    const statusElement = document.getElementById('authStatus');
    const recommendationsElement = document.getElementById('recommendations');
    
    if (!statusElement) {
        console.error('[SETTINGS] authStatus element not found!');
        return;
    }
    
    const indicator = statusElement.querySelector('.status-indicator');
    const text = statusElement.querySelector('span');
    
    if (!indicator || !text) {
        console.error('[SETTINGS] Auth status child elements not found!');
        return;
    }
    
    if (status.authenticated) {
        indicator.className = 'status-indicator status-authenticated';
        const methodNames = (status.methods || []).map(m => String(m.method ?? '')).join(', ');
        text.textContent = 'Authenticated (' + methodNames + ')';
        console.log('[SETTINGS] Set to authenticated with methods:', methodNames);
    } else {
        indicator.className = 'status-indicator status-unauthenticated';
        text.textContent = 'Not authenticated - using anonymous access';
        console.log('[SETTINGS] Set to unauthenticated');
    }
    
    // Build recommendations list via DOM APIs to avoid HTML injection.
    recommendationsElement.replaceChildren();
    if (status.recommendations && status.recommendations.length > 0) {
        const ul = document.createElement('ul');
        for (const rec of status.recommendations) {
            const li = document.createElement('li');
            li.textContent = String(rec ?? '');
            ul.appendChild(li);
        }
        recommendationsElement.appendChild(ul);
    }
}

function addRepository() {
    const url = repoUrlInput.value.trim();
    const branch = repoBranchInput.value.trim();
    
    console.log('[SETTINGS] addRepository called:', url, branch);
    
    if (!url) {
        return;
    }

    const parsed = parseRepositoryUrl(url);
    if (!parsed) {
        return;
    }

    vscode.postMessage({
        type: 'addRepository',
        owner: parsed.owner,
        name: parsed.name,
        branch: branch || undefined
    });

    // Clear form
    repoUrlInput.value = '';
    repoBranchInput.value = '';
}



function removeRepository(owner, name) {
    console.log('[SETTINGS] removeRepository function called with:', { owner, name });
    
    // const confirmed = confirm('Remove ' + owner + '/' + name + ' from knowledge base?');
    const confirmed = true
    console.log('[SETTINGS] User confirmed removal:', confirmed);
    
    if (confirmed) {
        console.log('[SETTINGS] Sending removeRepository message to extension');
        const message = {
            type: 'removeRepository',
            owner: owner,
            name: name
        };
        console.log('[SETTINGS] Message object:', message);
        
        vscode.postMessage(message);
        console.log('[SETTINGS] Message sent successfully');
    } else {
        console.log('[SETTINGS] User cancelled removal');
    }
}


function toggleRepository(owner, name, enabled) {
    vscode.postMessage({
        type: 'toggleRepository',
        owner: owner,
        name: name,
        enabled: enabled
    });
}

function indexRepository(owner, name, branch) {
    console.log('[SETTINGS] indexRepository called:', owner, name, branch);
    vscode.postMessage({
        type: 'indexRepository',
        owner: owner,
        name: name,
        branch: branch
    });
}

function refreshAllRepositories() {
    console.log('[SETTINGS] Refresh all clicked');
    vscode.postMessage({
        type: 'refreshRepositories'
    });
}

function parseRepositoryUrl(input) {
    // Handle different formats:
    // - owner/name
    // - https://github.com/owner/name
    const patterns = [
        /^([^\/]+)\/([^\/]+)$/,  // owner/name
        /github\.com\/([^\/]+)\/([^\/]+?)(?:\.git)?(?:\/.*)?$/  // GitHub URL
    ];

    for (const pattern of patterns) {
        const match = input.match(pattern);
        if (match) {
            return {
                owner: match[1],
                name: match[2]
            };
        }
    }

    return null;
}

function syncRepositories() {
    console.log('[SETTINGS] syncRepositories called');
    vscode.postMessage({
        type: 'syncRepositories'
    });
}

// Allow-list for status values used in CSS class names. Anything outside this
// set falls back to 'pending' so attacker-controlled status values can't be
// injected into the class attribute.
const ALLOWED_STATUSES = new Set(['pending', 'indexing', 'indexed', 'error']);

function renderRepositories() {
    console.log('[SETTINGS] renderRepositories called with:', repositories);

    // Clear previous contents without using innerHTML.
    repoContainer.replaceChildren();

    if (repositories.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'empty-state';
        const p = document.createElement('p');
        p.textContent = 'No repositories added yet. Add a repository above to get started.';
        empty.appendChild(p);
        repoContainer.appendChild(empty);
        return;
    }

    for (const repo of repositories) {
        const owner = String(repo.owner ?? '');
        const name = String(repo.name ?? '');
        const branch = repo.branch ? String(repo.branch) : '';
        const status = ALLOWED_STATUSES.has(repo.status) ? repo.status : 'pending';

        const item = document.createElement('div');
        item.className = 'repo-item';

        // Info section
        const info = document.createElement('div');
        info.className = 'repo-info';

        const nameEl = document.createElement('div');
        nameEl.className = 'repo-name';
        nameEl.textContent = `${owner}/${name}`;
        info.appendChild(nameEl);

        const statusEl = document.createElement('div');
        statusEl.className = 'repo-status';

        if (branch) {
            statusEl.appendChild(document.createTextNode(`Branch: ${branch} • `));
        }

        const indicator = document.createElement('span');
        indicator.className = `status-indicator status-${status}`;
        indicator.textContent = getStatusText(status);
        statusEl.appendChild(indicator);

        if (repo.lastIndexed) {
            const ts = new Date(repo.lastIndexed);
            const tsText = isNaN(ts.getTime()) ? '' : ts.toLocaleString();
            if (tsText) {
                statusEl.appendChild(document.createTextNode(` • Last indexed: ${tsText}`));
            }
        }

        info.appendChild(statusEl);
        item.appendChild(info);

        // Actions section
        const actions = document.createElement('div');
        actions.className = 'repo-actions';

        const toggleLabel = document.createElement('label');
        toggleLabel.className = 'toggle-switch';
        const toggleInput = document.createElement('input');
        toggleInput.type = 'checkbox';
        toggleInput.checked = !!repo.enabled;
        toggleInput.addEventListener('change', () => {
            toggleRepository(owner, name, toggleInput.checked);
        });
        const slider = document.createElement('span');
        slider.className = 'slider';
        toggleLabel.appendChild(toggleInput);
        toggleLabel.appendChild(slider);
        actions.appendChild(toggleLabel);

        const indexBtn = document.createElement('button');
        indexBtn.className = 'btn btn-secondary';
        indexBtn.textContent = status === 'indexing' ? 'Indexing...' : 'Index';
        indexBtn.disabled = status === 'indexing';
        indexBtn.addEventListener('click', () => indexRepository(owner, name, branch));
        actions.appendChild(indexBtn);

        const removeBtn = document.createElement('button');
        removeBtn.className = 'btn btn-danger';
        removeBtn.textContent = 'Remove';
        removeBtn.addEventListener('click', () => removeRepository(owner, name));
        actions.appendChild(removeBtn);

        item.appendChild(actions);
        repoContainer.appendChild(item);
    }
}

function getStatusText(status) {
    switch (status) {
        case 'indexing': return 'Indexing...';
        case 'indexed': return 'Indexed';
        case 'error': return 'Error';
        case 'pending':
        default: return 'Pending';
    }
}