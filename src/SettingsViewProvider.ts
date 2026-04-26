import * as vscode from 'vscode';
import { AgentService } from './AgentService';

interface Repository {
    owner: string;
    name: string;
    branch?: string;
    enabled: boolean;
    status?: 'indexing' | 'indexed' | 'error' | 'pending';
    lastIndexed?: string;
}

export class SettingsViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'archangel.settingsView';
    private _view?: vscode.WebviewView;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly agentService: AgentService
    ) {}

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

        // Handle messages from the webview
        webviewView.webview.onDidReceiveMessage(async (data) => {
            switch (data.type) {
                case 'addRepository':
                    await this.addRepository(data.owner, data.name, data.branch);
                    break;
                case 'removeRepository':
                    await this.removeRepository(data.owner, data.name);
                    break;
                case 'toggleRepository':
                    await this.toggleRepository(data.owner, data.name, data.enabled);
                    break;
                case 'indexRepository':
                    await this.indexRepository(data.owner, data.name, data.branch);
                    break;
                case 'refreshRepositories':
                    await this.refreshRepositories();
                    break;
                case 'getRepositories':
                    await this.sendRepositoriesToWebview();
                    break;
                case 'checkAuth':
                    await this.checkAuthStatus();
                    break;
                case 'authAction':
                    await this.useVSCodeAuth();
                    break;
                // Add this case to the existing switch statement in resolveWebviewView

                case 'syncRepositories':
                    await this.syncRepositoriesWithKnowledgeBase();
                    break;
            }
        });
        

        // Initial load
        setTimeout(async () => {
            try {
                await this.checkAuthStatus();
                await this.sendRepositoriesToWebview();
            } catch (error) {
                console.log('LSP server still starting up...');
            }
        }, 2000);
    }

    

    private async addRepository(owner: string, name: string, branch?: string) {
        const config = vscode.workspace.getConfiguration('archAngel');
        const repositories: Repository[] = config.get('knowledgeBase.repositories') || [];
        
        // Check if repository already exists
        const exists = repositories.some(repo => repo.owner === owner && repo.name === name);
        if (exists) {
            vscode.window.showWarningMessage(`Repository ${owner}/${name} is already in the knowledge base`);
            return;
        }

        // Add new repository
        const newRepo: Repository = {
            owner,
            name,
            branch,
            enabled: true,
            status: 'pending'
        };

        repositories.push(newRepo);
        await config.update('knowledgeBase.repositories', repositories, vscode.ConfigurationTarget.Global);
        
        vscode.window.showInformationMessage(`Added ${owner}/${name} to knowledge base`);
        this.sendRepositoriesToWebview();
    }


    private async toggleRepository(owner: string, name: string, enabled: boolean) {
        const config = vscode.workspace.getConfiguration('archAngel');
        const repositories: Repository[] = config.get('knowledgeBase.repositories') || [];
        
        const repo = repositories.find(r => r.owner === owner && r.name === name);
        if (repo) {
            repo.enabled = enabled;
            await config.update('knowledgeBase.repositories', repositories, vscode.ConfigurationTarget.Global);
            this.sendRepositoriesToWebview();
        }
    }

    // Add these methods to handle authentication and enhance the demo:

    private async checkAuthStatus() {
        try {
            const authStatus = await this.agentService.getAuthStatus();
            this._view?.webview.postMessage({
                type: 'authStatus',
                data: authStatus
            });
        } catch (error) {
            console.error('Failed to check auth status:', error);
            this._view?.webview.postMessage({
                type: 'authStatus',
                data: { authenticated: false, methods: [], recommendations: ['LSP server not available'] }
            });
        }
    }


    private async removeRepository(owner: string, name: string) {
        console.log('[SETTINGS-PROVIDER] removeRepository called with:', { owner, name });
        
        try {
            console.log('[SETTINGS-PROVIDER] Calling agentService.removeRepository...');
            
            // Remove from RAG (content store) first
            const ragRemovalSuccess = await this.agentService.removeRepository(owner, name);
            console.log('[SETTINGS-PROVIDER] RAG removal result:', ragRemovalSuccess);
            
            // Remove from VS Code settings (repository list)
            console.log('[SETTINGS-PROVIDER] Updating VS Code settings...');
            const config = vscode.workspace.getConfiguration('archAngel');
            const repositories: Repository[] = config.get('knowledgeBase.repositories') || [];
            console.log('[SETTINGS-PROVIDER] Current repositories:', repositories.length);
            
            const updatedRepositories = repositories.filter(repo => !(repo.owner === owner && repo.name === name));
            console.log('[SETTINGS-PROVIDER] Filtered repositories:', updatedRepositories.length);
            
            await config.update('knowledgeBase.repositories', updatedRepositories, vscode.ConfigurationTarget.Global);
            console.log('[SETTINGS-PROVIDER] Settings updated successfully');
            
            // Update the webview
            this.sendRepositoriesToWebview();
            console.log('[SETTINGS-PROVIDER] Webview updated');
            
            // Show appropriate success/warning message
            if (ragRemovalSuccess) {
                vscode.window.showInformationMessage(`✅ Successfully removed ${owner}/${name} from knowledge base and RAG`);
            } else {
                vscode.window.showWarningMessage(`⚠️ Removed ${owner}/${name} from repository list, but RAG removal failed (repository may not have been indexed)`);
            }
        } catch (error) {
            console.error('[SETTINGS-PROVIDER] Error removing repository:', error);
            vscode.window.showErrorMessage(`❌ Failed to remove repository ${owner}/${name}: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    }

    

    private async useVSCodeAuth() {
        try {
            const session = await vscode.authentication.getSession('github', ['repo'], { createIfNone: true });
            console.log('VS Code auth success');
            if (session?.accessToken) {
                const result = await this.agentService.setAuthToken(session.accessToken);
                if (result.success) {
                    vscode.window.showInformationMessage('VS Code GitHub authentication configured!');
                    await this.checkAuthStatus();
                }
            }
        } catch (error) {
            console.error('VS Code auth failed:', error);
            vscode.window.showErrorMessage('Failed to use VS Code GitHub authentication');
        }
    }

    // Update the indexRepository method:
    private async indexRepository(owner: string, name: string, branch?: string) {
        try {
            // Update status to indexing
            await this.updateRepositoryStatus(owner, name, 'indexing');
            
            vscode.window.showInformationMessage(`Starting indexing of ${owner}/${name}...`);
            
            // Try to index with authentication
            const success = await this.agentService.indexRepositoryWithAuth(owner, name, branch);
            
            if (success) {
                await this.updateRepositoryStatus(owner, name, 'indexed', new Date().toISOString());
                vscode.window.showInformationMessage(`Successfully indexed ${owner}/${name}!`);
            } else {
                await this.updateRepositoryStatus(owner, name, 'error');
                vscode.window.showErrorMessage(`Failed to index ${owner}/${name}`);
            }
        } catch (error: unknown) {
            console.error('Repository indexing failed:', error);
            await this.updateRepositoryStatus(owner, name, 'error');
            
            let errorMessage: string;
            if (error instanceof Error) {
                errorMessage = error.message;
            } else if (typeof error === 'string') {
                errorMessage = error;
            } else {
                errorMessage = 'Unknown error occurred';
            }
            
            // Check if it's an auth issue and suggest solutions
            if (errorMessage.includes('rate limit') || errorMessage.includes('403')) {
                
                await this.useVSCodeAuth();
            
            } else {
                vscode.window.showErrorMessage(`Failed to index ${owner}/${name}: ${errorMessage}`);
            }
        }
    }

    // Update the message handler to include auth actions:
    


    private async updateRepositoryStatus(owner: string, name: string, status: Repository['status'], lastIndexed?: string) {
        const config = vscode.workspace.getConfiguration('archAngel');
        const repositories: Repository[] = config.get('knowledgeBase.repositories') || [];
        
        const repo = repositories.find(r => r.owner === owner && r.name === name);
        if (repo) {
            repo.status = status;
            if (lastIndexed) {
                repo.lastIndexed = lastIndexed;
            }
            await config.update('knowledgeBase.repositories', repositories, vscode.ConfigurationTarget.Global);
            this.sendRepositoriesToWebview();
        }
    }

    private async refreshRepositories() {
        const config = vscode.workspace.getConfiguration('archAngel');
        const repositories: Repository[] = config.get('knowledgeBase.repositories') || [];
        
        for (const repo of repositories.filter(r => r.enabled)) {
            await this.indexRepository(repo.owner, repo.name, repo.branch);
        }
    }

    // Replace the sendRepositoriesToWebview method with this enhanced version

    public async sendRepositoriesToWebview() {
        if (!this._view) return;
        
        try {
            // Get repositories from VS Code settings
            const config = vscode.workspace.getConfiguration('archAngel');
            const settingsRepositories: Repository[] = config.get('knowledgeBase.repositories') || [];
            
            // Get actually indexed repositories from the knowledge base
            const indexedRepositoryKeys = await this.agentService.getIndexedRepositories();
            console.log('[SETTINGS-PROVIDER] Indexed repositories from KB:', indexedRepositoryKeys);
            
            // Parse indexed repository keys (format: "owner/name:branch")
            const indexedRepos = indexedRepositoryKeys.map(key => {
                const [repoPath, branch] = key.split(':');
                const [owner, name] = repoPath.split('/');
                return { owner, name, branch: branch !== 'main' ? branch : undefined };
            });
            
            // Create a map of indexed repositories for quick lookup
            const indexedRepoMap = new Map<string, boolean>();
            indexedRepos.forEach(repo => {
                const key = `${repo.owner}/${repo.name}`;
                indexedRepoMap.set(key, true);
            });
            
            // Synchronize settings repositories with actual knowledge base state
            const synchronizedRepositories: Repository[] = [];
            
            // First, add all repositories from settings and update their status
            for (const repo of settingsRepositories) {
                const repoKey = `${repo.owner}/${repo.name}`;
                const isIndexed = indexedRepoMap.has(repoKey);
                
                synchronizedRepositories.push({
                    ...repo,
                    status: isIndexed ? 'indexed' : (repo.status === 'indexing' ? 'indexing' : 'error')
                });
                
                // Mark as processed
                indexedRepoMap.delete(repoKey);
            }
            
            // Then, add any repositories that are indexed but not in settings (orphaned repositories)
            for (const [repoKey, _] of indexedRepoMap) {
                const indexedRepo = indexedRepos.find(r => `${r.owner}/${r.name}` === repoKey);
                if (indexedRepo) {
                    console.log(`[SETTINGS-PROVIDER] Found orphaned repository in KB: ${repoKey}`);
                    synchronizedRepositories.push({
                        owner: indexedRepo.owner,
                        name: indexedRepo.name,
                        branch: indexedRepo.branch,
                        enabled: true,
                        status: 'indexed',
                        lastIndexed: new Date().toISOString()
                    });
                }
            }
            
            // Update VS Code settings if there are orphaned repositories
            if (indexedRepoMap.size > 0) {
                await config.update('knowledgeBase.repositories', synchronizedRepositories, vscode.ConfigurationTarget.Global);
                console.log(`[SETTINGS-PROVIDER] Added ${indexedRepoMap.size} orphaned repositories to settings`);
            }
            
            // Remove repositories from settings that failed to index and are not in the knowledge base
            const finalRepositories = synchronizedRepositories.filter(repo => {
                // Keep repositories that are indexed, currently indexing, or pending
                return repo.status === 'indexed' || repo.status === 'indexing' || repo.status === 'pending';
            });
            
            // Update settings if we removed failed repositories
            if (finalRepositories.length !== synchronizedRepositories.length) {
                await config.update('knowledgeBase.repositories', finalRepositories, vscode.ConfigurationTarget.Global);
                const removedCount = synchronizedRepositories.length - finalRepositories.length;
                console.log(`[SETTINGS-PROVIDER] Removed ${removedCount} failed repositories from settings`);
            }
            
            // Send the synchronized list to the webview
            this._view.webview.postMessage({
                type: 'repositoriesUpdate',
                data: finalRepositories
            });
            
            console.log(`[SETTINGS-PROVIDER] Sent ${finalRepositories.length} synchronized repositories to webview`);
            
        } catch (error) {
            console.error('[SETTINGS-PROVIDER] Error synchronizing repositories:', error);
            
            // Fallback to settings-only mode
            const config = vscode.workspace.getConfiguration('archAngel');
            const repositories: Repository[] = config.get('knowledgeBase.repositories') || [];
            
            this._view.webview.postMessage({
                type: 'repositoriesUpdate',
                data: repositories
            });
        }
    }

    // Add this new method for manual sync
    private async syncRepositoriesWithKnowledgeBase() {
        console.log('[SETTINGS-PROVIDER] Manually syncing repositories with knowledge base...');
        await this.sendRepositoriesToWebview();
        vscode.window.showInformationMessage('✅ Knowledge base synchronized with UI');
    }

    public async addRepositoryFromCommand() {
        const repositoryUrl = await vscode.window.showInputBox({
            prompt: 'Enter GitHub repository URL or owner/name',
            placeHolder: 'e.g., microsoft/vscode or https://github.com/microsoft/vscode',
            validateInput: (value) => {
                if (!value) return 'Repository URL is required';
                if (!this.parseRepositoryUrl(value)) return 'Invalid repository format';
                return null;
            }
        });

        if (!repositoryUrl) return;

        const parsed = this.parseRepositoryUrl(repositoryUrl);
        if (!parsed) return;

        const branch = await vscode.window.showInputBox({
            prompt: 'Enter branch name (optional)',
            placeHolder: 'main, develop, etc. (leave empty for default branch)'
        });

        await this.addRepository(parsed.owner, parsed.name, branch || undefined);
    }

    private parseRepositoryUrl(input: string): { owner: string, name: string } | null {
        // Handle different formats:
        // - owner/name
        // - https://github.com/owner/name
        // - https://github.com/owner/name.git
        
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

    private _getHtmlForWebview(webview: vscode.Webview) {
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'src', 'settings.js')
        );

        return `<!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>ArchAngel Settings</title>
        <style>
            body {
                font-family: var(--vscode-font-family);
                font-size: var(--vscode-font-size);
                line-height: 1.6;
                color: var(--vscode-foreground);
                background-color: var(--vscode-editor-background);
                margin: 0;
                padding: 15px;
            }
            .section {
                margin-bottom: 20px;
                padding: 15px;
                border: 1px solid var(--vscode-panel-border);
                border-radius: 4px;
            }
            .auth-status {
                display: flex;
                align-items: center;
                gap: 10px;
                margin-bottom: 15px;
            }
            .status-indicator {
                width: 12px;
                height: 12px;
                border-radius: 50%;
            }
            .status-authenticated { background-color: #4CAF50; }
            .status-unauthenticated { background-color: #f44336; }
            .auth-buttons {
                display: flex;
                gap: 10px;
                flex-wrap: wrap;
                margin-top: 10px;
            }
            .btn {
                padding: 8px 16px;
                border: none;
                border-radius: 4px;
                cursor: pointer;
                font-family: inherit;
                margin-right: 8px;
            }
            .btn-primary {
                background-color: var(--vscode-button-background);
                color: var(--vscode-button-foreground);
            }
            .btn-primary:hover {
                background-color: var(--vscode-button-hoverBackground);
            }
            .btn-secondary {
                background-color: var(--vscode-button-secondaryBackground);
                color: var(--vscode-button-secondaryForeground);
            }
            .demo-section {
                background-color: var(--vscode-editor-inactiveSelectionBackground);
                border-left: 4px solid var(--vscode-textPreformat-foreground);
            }
            .demo-repos {
                display: grid;
                gap: 10px;
                margin-top: 15px;
            }
            .demo-repo {
                padding: 10px;
                border: 1px dashed var(--vscode-panel-border);
                border-radius: 4px;
                cursor: pointer;
                transition: background-color 0.2s;
            }
            .demo-repo:hover {
                background-color: var(--vscode-list-hoverBackground);
            }
            .form-group {
                margin-bottom: 10px;
            }
            .form-group input {
                width: 100%;
                padding: 8px;
                border: 1px solid var(--vscode-input-border);
                border-radius: 4px;
                background-color: var(--vscode-input-background);
                color: var(--vscode-input-foreground);
                box-sizing: border-box;
            }
            .repo-item {
                display: flex;
                justify-content: space-between;
                align-items: center;
                padding: 10px;
                margin-bottom: 10px;
                border: 1px solid var(--vscode-panel-border);
                border-radius: 4px;
                background-color: var(--vscode-list-activeSelectionBackground);
            }
            .repo-status {
                font-size: 12px;
                padding: 2px 6px;
                border-radius: 3px;
                margin-left: 10px;
            }
            .status-indexed { background-color: #4CAF50; color: white; }
            .status-indexing { background-color: #FF9800; color: white; }
            .status-error { background-color: #f44336; color: white; }
            .status-pending { background-color: #9E9E9E; color: white; }
            .recommendations {
                font-size: 12px;
                color: var(--vscode-descriptionForeground);
                margin-top: 10px;
            }
        </style>
    </head>
    <body>
        <!-- Authentication Section -->
        <div class="section">
            <h3>🔐 GitHub Authentication</h3>
            <div class="auth-status" id="authStatus">
                <div class="status-indicator status-unauthenticated"></div>
                <span>Checking authentication...</span>
            </div>
            <div class="auth-buttons">
                <button class="btn btn-primary" onclick="useVSCodeAuth()">Use VS Code GitHub</button>
                <button class="btn btn-secondary" onclick="checkAuthStatus()">Refresh Status</button>
            </div>
            <div class="recommendations" id="recommendations"></div>
        </div>

        <!-- Demo Section -->
        <div class="section demo-section">
            <h3>🚀 Quick Demo</h3>
            <p>Try indexing these popular repositories:</p>
            <div class="demo-repos">
                <div class="demo-repo" onclick="addDemoRepo('microsoft', 'vscode')">
                    <strong>microsoft/vscode</strong><br>
                    <small>VS Code source code - Large TypeScript project</small>
                </div>
                <div class="demo-repo" onclick="addDemoRepo('facebook', 'react')">
                    <strong>facebook/react</strong><br>
                    <small>React library - Popular JavaScript framework</small>
                </div>
                <div class="demo-repo" onclick="addDemoRepo('torvalds', 'linux')">
                    <strong>torvalds/linux</strong><br>
                    <small>Linux kernel - Large C project</small>
                </div>
            </div>
        </div>

        <!-- Repository Management -->
        <div class="section">
            <h3>📚 Knowledge Base Repositories</h3>
            
            <div class="form-group">
                <input type="text" id="repoUrl" placeholder="Repository owner/name (e.g., microsoft/vscode)" />
            </div>
            <div class="form-group">
                <input type="text" id="repoBranch" placeholder="Branch (optional, defaults to main)" />
            </div>
            <button class="btn btn-primary" id="addRepoBtn">Add Repository</button>
            <button class="btn btn-secondary" id="refreshAllBtn">Refresh All</button>
            <button class="btn btn-secondary" onclick="syncRepositories()">Sync with Knowledge Base</button>
            
            <div id="repoContainer" style="margin-top: 20px;">
                <!-- Repositories will be populated here -->
            </div>
        </div>
        <script src="${scriptUri}"></script>
    </body>
    </html>`;
    }
}