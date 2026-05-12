import * as vscode from 'vscode';
import * as path from 'path';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TextDocumentSyncKind,
    TransportKind
} from 'vscode-languageclient/node';


export interface ChatRequest {
    message: string;
    codeContext?: string;
    filePath?: string;
    includeRAG?: boolean;        
    repositoryFilter?: string;   
}
export interface DocRequest {
    docPath: string; 
    fileName: string;
}

export interface ChatResponse {
    message: string;
    sessionId: string;
    suggestions?: any[];
    sources?: ContentSource[];   
    ragContext?: string;         
}


export interface PingMultipleRequest {
    message: string;
    number: number;
    optional?: string;
}
export interface PingMultipleResponse {
    response: string;
    message: string;
    number: number;
    optional?: string;
    timestamp: string;
}

export interface IndexRepositoryRequest {
    owner: string;
    name: string;
    branch?: string;
}

export interface IndexRepositoryResponse {
    success: boolean;
    message: string;
    indexedFiles?: number;
}

export interface AuthStatusResponse {
    authenticated: boolean;
    methods: Array<{
        method: string;
        status: string;
        source: string;
    }>;
    recommendations: string[];
}

export interface SetAuthResponse {
    success: boolean;
    message?: string;
    error?: string;
}



export interface ContentSource {
    id: string;
    filePath: string;
    repository: string;
    language: string;
    chunkIndex: number;
    startLine: number;
    endLine: number;
    relevanceScore?: number;
    githubUrl?: string;
}


export interface RemoveRepositoryRequest {
    owner: string;
    name: string;
    branch?: string;
}

export interface RemoveRepositoryResponse {
    success: boolean;
    message: string;
    repositoryKey: string;
}

export interface GenerateDocResponse{
    success: boolean;
    docPath: string;
}


export class AgentService {
    private client?: LanguageClient;
    private context: vscode.ExtensionContext;

    constructor(context: vscode.ExtensionContext) {
        this.context = context;
    }

    async start(): Promise<void> {
        if (this.client) {
            return; // Already started
        }

        // Path to your C# LSP server
        const serverPath = path.join(
            this.context.extensionPath,
            'ArchAngel.Service/ArchAngel.Service'
        );


        console.log('Trying to start service at:', serverPath);
    
        // Use the compiled executable directly instead of 'dotnet run' to avoid build output
        const serverExePath = path.join(
            serverPath,
            'bin/Debug/net9.0/ArchAngel.Service.exe'
        );

        console.log('Trying to start executable at:', serverExePath);

        // Configure the server options to use the compiled executable
        const serverOptions: ServerOptions = {
            command: serverExePath,
            args: [],
            options: {
                cwd: serverPath
            },
            transport: TransportKind.stdio
        };

        // Configure client options
        const clientOptions: LanguageClientOptions = {
            documentSelector: [
                { scheme: 'file', language: 'typescript' },
                { scheme: 'file', language: 'typescriptreact' },
                { scheme: 'file', language: 'javascript' },
                { scheme: 'file', language: 'javascriptreact' },
                { scheme: 'file', language: 'python' },
                { scheme: 'file', language: 'csharp' },
                { scheme: 'file', language: 'java' },
                { scheme: 'file', language: 'cpp' },
                { scheme: 'file', language: 'c' },
                { scheme: 'file', language: 'go' },
                { scheme: 'file', language: 'rust' },
                { scheme: 'file', language: 'html' },
                { scheme: 'file', language: 'css' },
                { scheme: 'file', language: 'scss' },
                { scheme: 'file', language: 'json' },
                { scheme: 'file', language: 'xml' },
                { scheme: 'file', language: 'yaml' },
                { scheme: 'file', language: 'markdown' }
            ],
            synchronize: {
                configurationSection: 'archAngel',
                fileEvents: vscode.workspace.createFileSystemWatcher('**/*')
            },
            initializationOptions: {
                // Tell the server we want full document sync
                textDocumentSync: TextDocumentSyncKind.Full,
                // serverRole: 'supplementa'
            },
            // Add output channel for debugging
            outputChannel: vscode.window.createOutputChannel('ArchAngel LSP'),
            // Enable tracing for debugging
            traceOutputChannel: vscode.window.createOutputChannel('ArchAngel LSP Trace')
        };

        // Create and start the client
        this.client = new LanguageClient(
            'archAngel',
            'ArchAngel Language Server',
            serverOptions,
            clientOptions
        );

        // Add error handling and debugging
        this.client.onDidChangeState(event => {
            console.log(`LSP Client state changed: ${event.oldState} -> ${event.newState}`);
        });
        try {
            await this.client.start();
        } catch  (error: any) {
            vscode.window.showErrorMessage(
                'ArchAngel: Server failed to start. Ensure AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT, and AZURE_OPENAI_EMBEDDING_DEPLOYMENT environment variables are set, then restart VS Code.',
                'Copy Setup Commands'
            ).then(selection => {
                if (selection === 'Copy Setup Commands') {
                    vscode.env.clipboard.writeText(
                        `$env:AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"\n$env:AZURE_OPENAI_DEPLOYMENT="gpt-4o"\n$env:AZURE_OPENAI_EMBEDDING_DEPLOYMENT="text-embedding-3-large"`
                    );
                    vscode.window.showInformationMessage('Setup commands copied to clipboard');
                }
            });
            this.client = undefined;
            throw error;
        }
        console.log('ArchAngel LSP client started successfully');
    }

    async stop(): Promise<void> {
        if (this.client) {
            await this.client.stop();
            this.client = undefined;
        }
    }

    
    async removeRepository(owner: string, name: string, branch?: string): Promise<boolean> {
        if (!this.client) {
            console.error('LSP client not available for removeRepository');
            return false;
        }

        try {
            const request: RemoveRepositoryRequest = {
                owner,
                name,
                branch
            };

            const response = await this.client.sendRequest('archAngel/removeRepository', request) as RemoveRepositoryResponse;
            
            if (response.success) {
                console.log(`✅ Repository removed from RAG: ${response.repositoryKey}`);
                return true;
            } else {
                console.error(`❌ Failed to remove repository from RAG: ${response.message}`);
                return false;
            }
        } catch (error) {
            console.error('Error removing repository from RAG:', error);
            return false;
        }
    }

    async testNotification(): Promise<void> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        console.log('🔔 Sending test notification...');
        
        try {
            // Send a test notification with simple data
            await this.client.sendNotification('test/notification');
            
            console.log('✅ Test notification sent successfully');
            
            // Wait a bit to see if server responds
            await new Promise(resolve => setTimeout(resolve, 1000));
            
        } catch (error) {
            console.error('❌ Failed to send test notification:', error);
            throw error;
        }
    }

    async testDocumentSync(): Promise<void> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        // Get the currently active text document
        const activeEditor = vscode.window.activeTextEditor;
        if (!activeEditor) {
            throw new Error('No active text editor');
        }

        const document = activeEditor.document;
        console.log(`Testing document sync for: ${document.fileName}`);
        console.log(`Document language: ${document.languageId}`);
        console.log(`Document content length: ${document.getText().length}`);

        // Check if the document matches our document selector
        const documentSelector = [
            { scheme: 'file', language: 'typescript' },
            { scheme: 'file', language: 'javascript' },
            { scheme: 'file', language: 'python' },
            { scheme: 'file', language: 'csharp' }
        ];

        const isSupported = documentSelector.some(selector => 
            (selector.scheme === document.uri.scheme || !selector.scheme) &&
            (selector.language === document.languageId || !selector.language)
        );

        console.log(`Document is supported by LSP: ${isSupported}`);
        
        if (!isSupported) {
            throw new Error(`Document language '${document.languageId}' not supported by LSP server`);
        }

        // Try to manually send a test request to the server
        try {
            const testResult = await this.client.sendRequest('test/simple', {});
            console.log('Manual LSP test successful:', testResult);
        } catch (error) {
            console.error('Manual LSP test failed:', error);
            throw error;
        }
    }

    
    async testPing(): Promise<string> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            const response = await this.client.sendRequest('archAngel/ping', 'hello from client');
            return response as string;
        } catch (error) {
            console.error('Ping request failed:', error);
            throw error;
        }
    }
    
    
    async testPingMultiple(): Promise<string> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            // This should be sent as a SINGLE request object, not as separate parameters
            const request: PingMultipleRequest = {
                message: 'hello',
                number: 42,
                optional: 'optional value'
            };
            
            // The key is that this sends ONE parameter (the request object)
            // not three separate parameters
            const response = await this.client.sendRequest('archAngel/pingMultiple', request) as PingMultipleResponse;
            return response.response;
        } catch (error) {
            console.error('PingMultiple request failed:', error);
            throw error;
        }
    }

    // Add these methods to the AgentService class:
    async getAuthStatus(): Promise<AuthStatusResponse> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            const response = await this.client.sendRequest('archAngel/getAuthStatus', {});
            console.log("status: ", response);
            return response as AuthStatusResponse;
        } catch (error) {
            console.error('Get auth status request failed:', error);
            throw new Error('Failed to get authentication status');
        }
    }

    async setAuthToken(token: string): Promise<SetAuthResponse> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            const response = await this.client.sendRequest('archAngel/setAuthToken', token);
            return response as SetAuthResponse;
        } catch (error) {
            console.error('Set auth token request failed:', error);
            throw new Error('Failed to set authentication token');
        }
    }

    async indexRepositoryWithAuth(owner: string, name: string, branch?: string, authToken?: string): Promise<boolean> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            const request = {
                owner,
                name,
                branch,
            };
            
            const responsePromise = this.client.sendRequest('archAngel/indexRepository', request);
            const intervalId = setInterval(async () =>{
                await this.client!.sendRequest('archAngel/rpcPrompt');
                
                console.log(`PINGING`);
            }, 100);
            const response = await responsePromise as IndexRepositoryResponse;
            clearInterval(intervalId);
            return response.success;
        } catch (error) {
            console.error('Index repository request failed:', error);
            throw new Error(`Failed to index repository ${owner}/${name}: ${error}`);
        }
    }
    
    async chat(request: ChatRequest): Promise<ChatResponse> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            console.log("REQ:", request);
            const responseP = request.includeRAG ? this.client.sendRequest('archAngel/chatWithRAG', request) : this.client.sendRequest('archAngel/chat', request);
            const intervalId = setInterval(async () =>{
                await this.client!.sendRequest('archAngel/rpcPrompt');
                console.log(`PINGING`);
            }, 100);

            const response = await responseP;
            clearInterval(intervalId);
            console.error(response);
            return response as ChatResponse;
        } catch (error) {
            console.error('Chat request failed:', error);
            throw new Error('Failed to communicate with ArchAngel service');
        }
    }

    
    async indexRepository(owner: string, name: string, branch?: string): Promise<boolean> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            const request: IndexRepositoryRequest = {
                owner,
                name,
                branch
            };
            const responsePromise = this.client.sendRequest('archAngel/indexRepository', request);
            const intervalId = setInterval(async () =>{
                await this.client!.sendRequest('archAngel/rpcPrompt');
                console.log(`PINGING`);
            }, 100);
            const response = await responsePromise as IndexRepositoryResponse;
            clearInterval(intervalId);
            return response.success;
        } catch (error) {
            console.error('Index repository request failed:', error);
            throw new Error(`Failed to index repository ${owner}/${name}: ${error}`);
        }
    }

    isReady(): boolean {
        return this.client?.isRunning() ?? false;
    }



    async getIndexedRepositories(): Promise<string[]> {
        if (!this.client) {
            console.error('LSP client not available for getIndexedRepositories');
            return [];
        }

        try {
            const response = await this.client.sendRequest('archAngel/getIndexedRepositories', {}) as {
                success: boolean;
                repositories: string[];
                error?: string;
            };

            if (response.success) {
                console.log(`📋 Retrieved ${response.repositories.length} indexed repositories from knowledge base`);
                return response.repositories;
            } else {
                console.error(`❌ Failed to get indexed repositories: ${response.error}`);
                return [];
            }
        } catch (error) {
            console.error('Error getting indexed repositories:', error);
            return [];
        }
    }

    async indexFromConfig(): Promise<any> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            console.log('[AGENT] Requesting config-based indexing...');

            const responsePromise = this.client.sendRequest('archAngel/indexFromConfig', {});
            const intervalId = setInterval(async () =>{
                await this.client!.sendRequest('archAngel/rpcPrompt');
                
                console.log(`PINGING`);
            }, 100);
            const response = await responsePromise as IndexRepositoryResponse;
            clearInterval(intervalId);
            console.log('[AGENT] Config indexing response:', response);
            return response;
        } catch (error) {
            console.error('Config indexing failed:', error);
            throw error;
        }
    }

    async wipeKnowledgeBase(): Promise<any> {
        if (!this.client) {
            throw new Error('LSP client not started');
        }

        try {
            console.log('[AGENT] Requesting knowledge base wipe...');
            const response = await this.client.sendRequest('archAngel/wipeKnowledgeBase', {});
            console.log('[AGENT] Knowledge base wipe response:', response);
            return response;
        } catch (error) {
            console.error('Knowledge base wipe failed:', error);
            throw error;
        }
    }

    

    
    async generateCodeStyleDoc(fileName: string): Promise<any>{
        const r = vscode.workspace.workspaceFolders?.at(0)?.uri;
        
        if (!this.client) {
            throw new Error('LSP client not started');
        }
        if(r)
        {  
            const request: DocRequest = {
                docPath: r.fsPath,
                fileName: fileName
            }
            const pathPromise = this.client.sendRequest('archAngel/generateCodeStyleDoc', request);
            const intervalId = setInterval(async () =>{
                await this.client!.sendRequest('archAngel/rpcPrompt');
                
                console.log(`PINGING`);
            }, 100);
            const path = await pathPromise as GenerateDocResponse;
            clearInterval(intervalId);
            return path;
        }
    }

    async generateWikiDoc(fileName: string): Promise<any>{
        const r = vscode.workspace.workspaceFolders?.at(0)?.uri;
        
        if (!this.client) {
            throw new Error('LSP client not started');
        }
        if(r)
        {  
            const request: DocRequest = {
                docPath: r.fsPath,
                fileName
            }
            const pathPromise = this.client.sendRequest('archAngel/generateWikiDoc', request);
            const intervalId = setInterval(async () =>{
                await this.client!.sendRequest('archAngel/rpcPrompt');
                
                console.log(`PINGING`);
            }, 100);
            const path = await pathPromise as GenerateDocResponse;
            clearInterval(intervalId);
            return path;
        }
    }
}