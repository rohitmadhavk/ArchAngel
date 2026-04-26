import * as vscode from 'vscode';
import { AgentService } from './AgentService';
import { ChatViewProvider } from './ChatViewProvider';
import { SettingsViewProvider } from './SettingsViewProvider';
import path from 'path';

let agentService: AgentService;

export async function activate(context: vscode.ExtensionContext) {
    console.log('ArchAngel is now active!');
    
    agentService = new AgentService(context);
    
    // Start the LSP client
    try {
        await agentService.start();
        vscode.window.showInformationMessage('ArchAngel service started successfully!');
    } catch (error) {
        vscode.window.showErrorMessage(`Failed to start ArchAngel: ${error}`);
        return;
    }

    // Register the chat view provider
    const chatViewProvider = new ChatViewProvider(context.extensionUri, agentService);
    try {
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider(ChatViewProvider.viewType, chatViewProvider)
        );
        vscode.window.showInformationMessage('ArchAngel service started successfully!');
    } catch (error) {
        vscode.window.showErrorMessage(`Failed to start ArchAngel: ${error}`);
        return;
    }
    

    const settingsProvider = new SettingsViewProvider(context.extensionUri, agentService);
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(SettingsViewProvider.viewType, settingsProvider)
    );


    
    



    // Register the chat command (for command palette)
    const chatCommand = vscode.commands.registerCommand('archangel.chat', async () => {
        if (!agentService.isReady()) {
            vscode.window.showWarningMessage('ArchAngel service is not ready');
            return;
        }

        const input = await vscode.window.showInputBox({
            prompt: 'Chat with ArchAngel',
            placeHolder: 'Ask me about your code, request a review, or ask for help...'
        });

        if (input) {
            try {
                const response = await agentService.chat({ message: input });
                vscode.window.showInformationMessage(`Assistant: ${response.message}`);
            } catch (error) {
                vscode.window.showErrorMessage(`Chat failed: ${error}`);
            }
        }
    });

    context.subscriptions.push(chatCommand);



    // Manually track document events and forward to LSP server
    const supportedLanguages = ['typescript', 'javascript', 'python', 'csharp'];

    


    // Also track already open documents when extension activates
    for (const document of vscode.workspace.textDocuments) {
        if (supportedLanguages.includes(document.languageId) && document.uri.scheme === 'file') {
            console.log(`📁 Already open document: ${document.fileName} (${document.languageId})`);
            try {
                await agentService.testNotification();
            } catch (error) {
                console.error('❌ Failed to forward existing document:', error);
            }
        }
    }
    


    // Register open chat command
    const openChatCommand = vscode.commands.registerCommand('archangel.openChat', () => {
        vscode.commands.executeCommand('archangel.chatView.focus');
    });

    context.subscriptions.push(openChatCommand);

    
    const indexFromConfigCommand = vscode.commands.registerCommand('archangel.indexFromConfig', async () => {
        try {
            vscode.window.showInformationMessage('🔄 Starting indexing from configuration...');
            
            const result = await agentService.indexFromConfig();
            
            if (result.success) {
                const message = `✅ Indexed ${result.indexedCount}/${result.totalCount} repositories from ${result.configName}`;
                vscode.window.showInformationMessage(message);
            } else {
                vscode.window.showErrorMessage(`❌ Failed to index from config: ${result.error}`);
            }
        } catch (error) {
            console.error('Index from config command failed:', error);
            vscode.window.showErrorMessage(`❌ Failed to index from config: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    });
    context.subscriptions.push(indexFromConfigCommand);


    const genenerateCodeStyleDoc = vscode.commands.registerCommand('archangel.generateCodeStyleDoc', async () => {
        try {
            const filename = await vscode.window.showInputBox({
                prompt: 'Enter filename for the generated document',
                value: 'code-style-guide.md',
                placeHolder: 'ArchAngelReport_CodeStyle.md'
            });
            if(!filename) return;

            vscode.window.showInformationMessage('🔄 Generating Code Style Doc...');
            
            const result = await agentService.generateCodeStyleDoc(filename);
            
            if (result.success) {
                const message = `✅ Generated Doc at ${result.docPath}`;
                vscode.window.showInformationMessage(message);
            } else {
                vscode.window.showErrorMessage(`❌ Failed to create document: ${result.error}`);
            }
        } catch (error) {
            console.error('Create document command failed:', error);
            vscode.window.showErrorMessage(`❌ Failed to create document:${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    });

    context.subscriptions.push(genenerateCodeStyleDoc);

    const genenerateWikiDoc = vscode.commands.registerCommand('archangel.generateWikiDoc', async () => {
        try {
            const filename = await vscode.window.showInputBox({
                prompt: 'Enter filename for the generated document',
                value: 'ArchAngelReport_Wiki.md',
                placeHolder: 'ArchAngelReport_Wiki.md'
            });
            if(!filename) return;
            vscode.window.showInformationMessage('🔄 Generating Wiki Doc...');
            
            const result = await agentService.generateWikiDoc(filename);
            
            if (result.success) {
                const message = `✅ Generated Doc at ${result.docPath}`;
                vscode.window.showInformationMessage(message);
            } else {
                vscode.window.showErrorMessage(`❌ Failed to create document: ${result.error}`);
            }
        } catch (error) {
            console.error('Create document command failed:', error);
            vscode.window.showErrorMessage(`❌ Failed to create document:${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    });

    context.subscriptions.push(genenerateWikiDoc);

    const wipeKnowledgeBaseCommand = vscode.commands.registerCommand('archangel.wipeKnowledgeBase', async () => {
        // Show confirmation dialog
        const choice = await vscode.window.showWarningMessage(
            '⚠️ This will permanently delete all indexed repositories from the knowledge base. This action cannot be undone.',
            { modal: true },
            'Wipe Knowledge Base',
            'Cancel'
        );

        if (choice !== 'Wipe Knowledge Base') {
            return;
        }

        try {
            vscode.window.showInformationMessage('🗑️ Wiping knowledge base...');
            
            const result = await agentService.wipeKnowledgeBase();
            
            if (result.success) {
                const message = `✅ Successfully wiped knowledge base. Deleted ${result.deletedFiles} database files.`;
                vscode.window.showInformationMessage(message);
                
                // Refresh the settings view to show empty state
                vscode.commands.executeCommand('archangel.refreshKnowledgeBase');
            } else {
                vscode.window.showErrorMessage(`❌ Failed to wipe knowledge base: ${result.error}`);
            }
        } catch (error) {
            console.error('Wipe knowledge base command failed:', error);
            vscode.window.showErrorMessage(`❌ Failed to wipe knowledge base: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    });

    context.subscriptions.push(wipeKnowledgeBaseCommand);
    
    
}

export function deactivate() {
    if (agentService) {
        agentService.stop();
    }
}