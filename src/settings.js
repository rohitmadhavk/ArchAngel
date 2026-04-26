console.log('[SETTINGS] Settings webview loaded');

const vscode = acquireVsCodeApi();

// DOM elements
const repoUrlInput = document.getElementById('repoUrl');
const repoBranchInput = document.getElementById('repoBranch');
const addRepoBtn = document.getElementById('addRepoBtn');
const refreshAllBtn = document.getElementById('refreshAllBtn');
const repoContainer = document.getElementById('repoContainer');

let repositories = [];

// Event listeners
addRepoBtn.addEventListener('click', addRepository);
refreshAllBtn.addEventListener('click', refreshAllRepositories);

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
        const methodNames = (status.methods || []).map(m => m.method).join(', ');
        text.textContent = 'Authenticated (' + methodNames + ')';
        console.log('[SETTINGS] Set to authenticated with methods:', methodNames);
    } else {
        indicator.className = 'status-indicator status-unauthenticated';
        text.textContent = 'Not authenticated - using anonymous access';
        console.log('[SETTINGS] Set to unauthenticated');
    }
    
    if (status.recommendations && status.recommendations.length > 0) {
        recommendationsElement.innerHTML = '<ul>' + 
            status.recommendations.map(rec => '<li>' + rec + '</li>').join('') + 
            '</ul>';
    } else {
        recommendationsElement.innerHTML = '';
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
// Add this function to settings.js

function syncRepositories() {
    console.log('[SETTINGS] syncRepositories called');
    vscode.postMessage({
        type: 'syncRepositories'
    });
}

function renderRepositories() {
    console.log('[SETTINGS] renderRepositories called with:', repositories);
    
    if (repositories.length === 0) {
        repoContainer.innerHTML = `
            <div class="empty-state">
                <p>No repositories added yet. Add a repository above to get started.</p>
            </div>
        `;
        return;
    }

    const html = repositories.map(repo => {
        // Debug: Log what we're trying to render for each repo
        console.log('[SETTINGS] Rendering repo:', repo.owner + '/' + repo.name);
        console.log('[SETTINGS] Owner:', JSON.stringify(repo.owner));
        console.log('[SETTINGS] Name:', JSON.stringify(repo.name));
        
        // Use double quotes for the outer template and escape any quotes in the data
        const safeOwner = repo.owner.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        const safeName = repo.name.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        const safeBranch = (repo.branch || '').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        
        const repoHtml = `
        <div class="repo-item">
            <div class="repo-info">
                <div class="repo-name">${repo.owner}/${repo.name}</div>
                <div class="repo-status">
                    ${repo.branch ? `Branch: ${repo.branch} • ` : ''}
                    <span class="status-indicator status-${repo.status || 'pending'}">
                        ${getStatusText(repo.status)}
                    </span>
                    ${repo.lastIndexed ? ` • Last indexed: ${new Date(repo.lastIndexed).toLocaleString()}` : ''}
                </div>
            </div>
            <div class="repo-actions">
                <label class="toggle-switch">
                    <input type="checkbox" ${repo.enabled ? 'checked' : ''} 
                           onchange="toggleRepository('${safeOwner}', '${safeName}', this.checked)">
                    <span class="slider"></span>
                </label>
                <button class="btn btn-secondary" 
                        onclick="indexRepository('${safeOwner}', '${safeName}', '${safeBranch}')"
                        ${repo.status === 'indexing' ? 'disabled' : ''}>
                    ${repo.status === 'indexing' ? 'Indexing...' : 'Index'}
                </button>
                <button class="btn btn-danger" 
                        onclick="removeRepository('${safeOwner}', '${safeName}')">
                    Remove
                </button>
            </div>
        </div>`;
        
        // Debug: Log the generated HTML for the remove button specifically
        const removeButtonMatch = repoHtml.match(/<button[^>]*onclick="removeRepository[^"]*"[^>]*>Remove<\/button>/);
        if (removeButtonMatch) {
            console.log('[SETTINGS] Generated remove button HTML:', removeButtonMatch[0]);
        }
        
        return repoHtml;
    }).join('');

    console.log('[SETTINGS] Complete HTML being set:', html);
    repoContainer.innerHTML = html;
    
    // Debug: Check if buttons were actually created
    const removeButtons = document.querySelectorAll('.btn-danger');
    console.log('[SETTINGS] Found', removeButtons.length, 'remove buttons after render');
    removeButtons.forEach((btn, index) => {
        console.log('[SETTINGS] Remove button', index, 'onclick:', btn.getAttribute('onclick'));
        
        // Try clicking programmatically to test
        console.log('[SETTINGS] Testing button', index, 'click handler...');
        try {
            // Test if the onclick would work
            const onclickAttr = btn.getAttribute('onclick');
            console.log('[SETTINGS] Would execute:', onclickAttr);
        } catch (e) {
            console.error('[SETTINGS] Button', index, 'onclick error:', e);
        }
    });
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