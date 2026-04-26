import * as vscode from 'vscode';
import { AgentService } from './AgentService';

export class ChatViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'archangel.chatView';
    private _view?: vscode.WebviewView;

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly agentService: AgentService
    ) {}

    private async getAvailableRepositories(): Promise<string[]> {
        try {
            // Get repositories from VS Code settings
            const config = vscode.workspace.getConfiguration('archAngel');
            const repositories = config.get<Array<{owner: string, name: string, status?: string, enabled?: boolean}>>('knowledgeBase.repositories') || [];
            
            // Filter for indexed and enabled repositories
            const availableRepos = repositories
                .filter(repo => repo.enabled && repo.status === 'indexed')
                .map(repo => `${repo.owner}/${repo.name}`);
            
            console.log('[EXTENSION] Available repositories:', availableRepos);
            return availableRepos;
        } catch (error) {
            console.error('[EXTENSION] Failed to get available repositories:', error);
            return [];
        }
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this._extensionUri, vscode.Uri.joinPath(this._extensionUri, 'src', 'webview')]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

        // Test message to webview after a short delay
        
        setTimeout(async () => {
            console.log('[EXTENSION] Sending test message to webview...');
            const testResult = webviewView.webview.postMessage({
                type: 'test',
                message: 'Test message from extension - webview communication working!'
            });
            console.log('[EXTENSION] Test message posted, result:', testResult);
        }, 1000);

        // Handle messages from the webview
        webviewView.webview.onDidReceiveMessage(async (data) => {
            console.log('[EXTENSION] Received message from webview:', data);
            
            switch (data.type) {
                case 'chat':
                    if (this.agentService.isReady()) {
                        try {
                            const availableRepos = await this.getAvailableRepositories()
                            console.log('[EXTENSION] Agent service is ready, sending chat request...');
                            
                            // Show typing indicator
                            console.log('[EXTENSION] Sending typing indicator (true)...');
                            const typingResult = webviewView.webview.postMessage({
                                type: 'typing',
                                isTyping: true
                            });
                            console.log('[EXTENSION] Typing message posted, result:', typingResult);

                            const response = await this.agentService.chat({ 
                                message: data.message,
                                includeRAG: availableRepos.length > 0,
                                filePath: vscode.window.activeTextEditor?.document.uri.toString()

                            });

                            console.log('[EXTENSION] Got response from agent service:', response);

                            // Validate response structure
                            if (!response || !response.message) {
                                console.error('[EXTENSION] Invalid response structure:', response);
                                throw new Error('Invalid response from agent service');
                            }

                            // Send response back to webview
                            console.log('[EXTENSION] Sending response to webview...');
                            const responseMessage = {
                                type: 'response',
                                message: response.message,
                                sources: response.sources,
                                ragUsed: availableRepos.length > 0,
                                repositoryCount: availableRepos.length,
                                id: response.sessionId || 'unknown'
                            };
                            console.log('[EXTENSION] Response message object:', responseMessage);
                            
                            const responseResult = webviewView.webview.postMessage(responseMessage);
                            console.log('[EXTENSION] Response message posted, result:', responseResult);
                            
                        } catch (error) {
                            console.error('[EXTENSION] Chat error:', error);
                            const errorMessage = {
                                type: 'error',
                                message: `Error: ${error instanceof Error ? error.message : String(error)}`
                            };
                            console.log('[EXTENSION] Sending error message:', errorMessage);
                            
                            const errorResult = webviewView.webview.postMessage(errorMessage);
                            console.log('[EXTENSION] Error message posted, result:', errorResult);
                        } finally {
                            // Hide typing indicator
                            console.log('[EXTENSION] Sending typing indicator (false)...');
                            const finalTypingResult = webviewView.webview.postMessage({
                                type: 'typing',
                                isTyping: false
                            });
                            console.log('[EXTENSION] Final typing message posted, result:', finalTypingResult);
                        }
                    } else {
                        console.log('[EXTENSION] Agent service is not ready');
                        const notReadyMessage = {
                            type: 'error',
                            message: 'ArchAngel service is not ready'
                        };
                        console.log('[EXTENSION] Sending not ready message:', notReadyMessage);
                        
                        const notReadyResult = webviewView.webview.postMessage(notReadyMessage);
                        console.log('[EXTENSION] Not ready message posted, result:', notReadyResult);
                    }
                    break;
                
                case 'ready':
                    // Webview is ready, send a welcome message
                    console.log('[EXTENSION] Webview reports ready, sending welcome message...');
                    webviewView.webview.postMessage({
                        type: 'response',
                        message: 'Welcome! I\'m your ArchAngel. How can I help you today?'
                    });
                    break;
                default:
                    console.warn('[EXTENSION] Unknown message type:', data.type);
            }
        });
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        // Get URI for the JavaScript file
        const scriptUri = webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'src', 'chat.js')
        );

        return `<!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src ${webview.cspSource}; style-src ${webview.cspSource} 'unsafe-inline';">
        <title>ArchAngel</title>
        <style>
            body {
                font-family: var(--vscode-font-family);
                font-size: var(--vscode-font-size);
                line-height: 1.6;
                color: var(--vscode-foreground);
                background-color: var(--vscode-editor-background);
                margin: 0;
                padding: 10px;
                height: 100vh;
                display: flex;
                flex-direction: column;
            }

            .chat-container {
                flex: 1;
                overflow-y: auto;
                padding: 10px 0;
                margin-bottom: 10px;
            }

            .message {
                margin-bottom: 15px;
                padding: 10px;
                border-radius: 8px;
                max-width: 100%;
            }

            .user-message {
                background-color: var(--vscode-input-background);
                border: 1px solid var(--vscode-input-border);
                margin-left: 20px;
            }

            .assistant-message {
                background-color: var(--vscode-editor-inactiveSelectionBackground);
                border-left: 3px solid var(--vscode-activityBar-activeBorder);
                margin-right: 20px;
            }

            .message-header {
                font-weight: bold;
                margin-bottom: 8px;
                font-size: 0.9em;
                opacity: 0.8;
            }

            .message-content {
                line-height: 1.6;
            }

            /* Enhanced Markdown Styles */
            // Replace the CSS section in _getHtmlForWebview method (around lines 175-250) with these enhanced styles:

            /* Enhanced Markdown Styles */
            .message-content h1, 
            .message-content h2, 
            .message-content h3, 
            .message-content h4, 
            .message-content h5, 
            .message-content h6 {
                margin: 20px 0 12px 0;
                font-weight: bold;
                color: var(--vscode-foreground);
                line-height: 1.3;
            }

            .message-content h1 { 
                font-size: 2em; 
                border-bottom: 2px solid var(--vscode-panel-border); 
                padding-bottom: 8px; 
            }
            .message-content h2 { 
                font-size: 1.75em; 
                border-bottom: 1px solid var(--vscode-panel-border); 
                padding-bottom: 6px; 
            }
            .message-content h3 { 
                font-size: 1.5em; 
                color: var(--vscode-terminal-ansiYellow);
            }
            .message-content h4 { 
                font-size: 1.3em; 
                color: var(--vscode-terminal-ansiCyan);
            }
            .message-content h5 { 
                font-size: 1.2em; 
            }
            .message-content h6 { 
                font-size: 1.1em; 
                color: var(--vscode-descriptionForeground);
            }

            .message-content p {
                margin: 12px 0;
                line-height: 1.6;
            }

            .message-content strong, .message-content b {
                font-weight: bold;
                color: var(--vscode-terminal-ansiYellow);
            }

            .message-content em, .message-content i {
                font-style: italic;
                color: var(--vscode-terminal-ansiCyan);
            }

            .message-content code {
                background-color: var(--vscode-textCodeBlock-background);
                color: var(--vscode-textPreformat-foreground);
                padding: 3px 6px;
                border-radius: 4px;
                font-family: var(--vscode-editor-font-family), 'Courier New', monospace;
                font-size: 0.9em;
                border: 1px solid var(--vscode-input-border);
            }

            .message-content pre {
                background-color: var(--vscode-textCodeBlock-background);
                color: var(--vscode-textPreformat-foreground);
                padding: 16px;
                border-radius: 8px;
                overflow-x: auto;
                margin: 16px 0;
                border: 1px solid var(--vscode-input-border);
                font-family: var(--vscode-editor-font-family), 'Courier New', monospace;
                font-size: 0.9em;
                line-height: 1.5;
            }

            .message-content pre code {
                background: none;
                padding: 0;
                border: none;
                font-size: inherit;
            }

            .message-content blockquote {
                border-left: 4px solid var(--vscode-terminal-ansiBlue);
                margin: 16px 0;
                padding-left: 20px;
                color: var(--vscode-descriptionForeground);
                font-style: italic;
                background-color: var(--vscode-editor-inactiveSelectionBackground);
                padding: 12px 20px;
                border-radius: 4px;
            }

            .message-content ul, .message-content ol {
                margin: 12px 0 12px 24px;
                padding-left: 0;
            }

            .message-content li {
                margin: 6px 0;
                line-height: 1.5;
            }

            /* Enhanced Table Styles */
            .message-content table {
                border-collapse: collapse;
                margin: 20px 0;
                width: 100%;
                font-size: 0.9em;
                box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                border-radius: 6px;
                overflow: hidden;
                border: 1px solid var(--vscode-input-border);
            }

            .message-content thead {
                background-color: var(--vscode-editor-selectionBackground);
            }

            .message-content th {
                background-color: var(--vscode-editor-selectionBackground);
                color: var(--vscode-foreground);
                font-weight: bold;
                padding: 12px 16px;
                text-align: left;
                border-bottom: 2px solid var(--vscode-panel-border);
                border-right: 1px solid var(--vscode-input-border);
                font-size: 0.95em;
            }

            .message-content th:last-child {
                border-right: none;
            }

            .message-content td {
                padding: 10px 16px;
                text-align: left;
                border-bottom: 1px solid var(--vscode-input-border);
                border-right: 1px solid var(--vscode-input-border);
                vertical-align: top;
            }

            .message-content td:last-child {
                border-right: none;
            }

            .message-content tbody tr:nth-child(even) {
                background-color: var(--vscode-editor-inactiveSelectionBackground);
            }

            .message-content tbody tr:hover {
                background-color: var(--vscode-list-hoverBackground);
            }

            .message-content tbody tr:last-child td {
                border-bottom: none;
            }

            .message-content a {
                color: var(--vscode-textLink-foreground);
                text-decoration: none;
                border-bottom: 1px dotted var(--vscode-textLink-foreground);
            }

            .message-content a:hover {
                color: var(--vscode-textLink-activeForeground);
                text-decoration: underline;
                border-bottom: 1px solid var(--vscode-textLink-activeForeground);
            }

            .message-content hr {
                border: none;
                border-top: 2px solid var(--vscode-panel-border);
                margin: 24px 0;
            }

            /* Source information styles */
            .message-sources {
                margin-top: 12px;
                padding-top: 12px;
                border-top: 1px solid var(--vscode-panel-border);
                font-size: 0.9em;
            }

            .sources-header {
                font-weight: bold;
                margin-bottom: 8px;
                color: var(--vscode-terminal-ansiGreen);
            }

            .source-item {
                margin: 4px 0;
                padding: 4px 8px;
                background-color: var(--vscode-editor-background);
                border-radius: 4px;
                border: 1px solid var(--vscode-input-border);
            }

            .source-number {
                font-weight: bold;
                margin-right: 8px;
                color: var(--vscode-terminal-ansiBlue);
            }

            .source-link {
                color: var(--vscode-textLink-foreground);
                text-decoration: none;
                font-family: var(--vscode-editor-font-family), monospace;
            }

            .source-link:hover {
                text-decoration: underline;
            }

            .source-details {
                color: var(--vscode-descriptionForeground);
                margin-left: 8px;
                font-size: 0.85em;
            }

            .source-repo {
                color: var(--vscode-terminal-ansiMagenta);
                font-weight: bold;
                margin-left: 8px;
                font-size: 0.85em;
            }

            .no-sources {
                margin-top: 8px;
                padding: 8px;
                background-color: var(--vscode-editor-background);
                border-radius: 4px;
                border: 1px solid var(--vscode-input-border);
                color: var(--vscode-descriptionForeground);
                font-style: italic;
            }

            .input-container {
                display: flex;
                gap: 8px;
                padding: 10px 0;
                border-top: 1px solid var(--vscode-panel-border);
            }

            .message-input {
                flex: 1;
                padding: 8px 12px;
                border: 1px solid var(--vscode-input-border);
                border-radius: 4px;
                background-color: var(--vscode-input-background);
                color: var(--vscode-input-foreground);
                font-family: inherit;
                font-size: inherit;
            }

            .send-button {
                padding: 8px 16px;
                background-color: var(--vscode-button-background);
                color: var(--vscode-button-foreground);
                border: none;
                border-radius: 4px;
                cursor: pointer;
                font-family: inherit;
            }

            .send-button:hover {
                background-color: var(--vscode-button-hoverBackground);
            }

            .send-button:disabled {
                opacity: 0.5;
                cursor: not-allowed;
            }

            .typing-indicator {
                padding: 10px;
                font-style: italic;
                opacity: 0.7;
                display: none;
            }

            .welcome-message {
                text-align: center;
                padding: 20px;
                opacity: 0.7;
            }

            .error-message {
                background-color: var(--vscode-inputValidation-errorBackground);
                border: 1px solid var(--vscode-inputValidation-errorBorder);
                color: var(--vscode-inputValidation-errorForeground);
            }

            .debug-info {
                position: fixed;
                top: 10px;
                right: 10px;
                background: rgba(255, 0, 0, 0.8);
                color: white;
                padding: 5px;
                font-size: 12px;
                z-index: 1000;
            }
        </style>
    </head>
    <body>
        <div class="debug-info" id="debugInfo">Webview Loading...</div>
        
        <div class="chat-container" id="chatContainer">
            <div class="welcome-message">
                <h3>🤖 ArchAngel</h3>
                <p>Ask me anything about your code, request reviews, or get coding help!</p>
            </div>
        </div>
        
        <div class="typing-indicator" id="typingIndicator">
            Assistant is typing...
        </div>
        
        <div class="input-container">
            <input 
                type="text" 
                id="messageInput" 
                class="message-input" 
                placeholder="Ask about your code..." 
                autocomplete="off"
            />
            <button id="sendButton" class="send-button">Send</button>
        </div>

        <script src="${scriptUri}"></script>
    </body>
    </html>`;
    }
    
}