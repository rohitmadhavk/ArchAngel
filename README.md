# ArchAngel

**AI-powered iterative coding assistant** — A VS Code extension that leverages Azure AI Foundry, Azure OpenAI, and Semantic Kernel to provide intelligent code analysis, guidance, and best-practice recommendations grounded in your team's "golden" repositories. Please keep in mind that this is a work in progress project that can and is intended to be configured to your needs. Please fork this project to build out your projects. 

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Development](#development)
- [Commands](#commands)
- [Known Issues](#known-issues)
- [Contributing](#contributing)

## Features

### 🤖 AI-Powered Chat
- Real-time conversational interface integrated directly in VS Code
- Context-aware responses grounded in your codebase and golden repositories
- Supports Azure AI Foundry and Semantic Kernel backends

### 📚 Knowledge Base Management
- Index GitHub repositories to build a searchable knowledge base
- SQLite-backed persistent storage per workspace (`.archAngel/knowledge.ca`)
- Semantic search using `text-embedding-3-large` embeddings
- RAG (Retrieval-Augmented Generation) with citation support

### 🔍 Code Analysis & Compliance
- Project compliance assessment against golden repositories
- Architectural guidance based on indexed best practices
- Quick compliance checks for rapid feedback
- Code style and wiki document generation from golden repos

### 💻 Code Completion
- AI-powered inline code completions via LSP
- Configurable completion model (defaults to `gpt-4o-mini`)

### 🌐 Multi-Language Support
Activates for 18 languages:
TypeScript, JavaScript, Python, C#, Java, C, C++, Go, Rust, HTML, CSS, SCSS, JSON, XML, YAML, Markdown, and their React variants

## Architecture

### High-Level Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    VS Code Extension                        │
│  (TypeScript/JavaScript — src/)                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Extension Host                                      │   │
│  │  - AgentService (LSP Client)                         │   │
│  │  - ChatViewProvider (Chat Webview)                   │   │
│  │  - SettingsViewProvider (Knowledge Base UI)          │   │
│  └──────────────────────────────────────────────────────┘   │
└────────────────────────┬────────────────────────────────────┘
                         │
                  LSP / JSON-RPC
                   (StreamJsonRpc)
                         │
┌────────────────────────▼────────────────────────────────────┐
│           ArchAngel.Service (.NET 9)                        │
│  ArchAngelLanguageServer (LSP Server)                       │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ Chat          — FoundryChatService / SKChatService   │   │
│  │ Completion    — FoundryCompletionService / SKCompletionService        │   │
│  │ DocumentGen   — SKDocumentGenerationService          │   │
│  │ Github        — RepositoryIndexer, ContentProcessor  │   │
│  │ Utils         — GoldenRepoSearchService, Citations   │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ Storage                                              │   │
│  │ - WorkspaceContentStore (SQLite + vector search)     │   │
│  │ - ContentStoreFactory / ContentStoreProvider         │   │
│  └──────────────────────────────────────────────────────┘   │
└────────────┬──────────────────────────┬─────────────────────┘
             │                          │
   ┌─────────▼──────────┐       ┌───────▼─────────┐
   │  Azure AI Foundry  │       │   GitHub API    │
   │  + Azure OpenAI    │       │                 │
   │  (GPT-4o, GPT-4o-  │       │  - Repository   │
   │  mini, Embeddings) │       │    fetching     │
   └────────────────────┘       └─────────────────┘
```

### Component Details

#### VS Code Extension Layer (`src/`)
| File | Purpose |
|---|---|
| `extension.ts` | Activation, command registration, view providers |
| `AgentService.ts` | LSP client — launches and communicates with the .NET backend |
| `ChatViewProvider.ts` | Webview for the chat panel |
| `SettingsViewProvider.ts` | Webview for knowledge base configuration |
| `chat.js` / `settings.js` | Frontend scripts for the webviews |

#### Backend Service Layer (`ArchAngel.Service/`)

**Chat** — Conversational AI with RAG and citation support (Choose your implementation via DI):
- `FoundryChatService` (default) — Azure AI Foundry
- `SKChatService` — Semantic Kernel
- `SKMemoryChatService` — Semantic Kernel with memory

**Completion** — Inline code completions:
- `FoundryCompletionService` (default) — Azure AI Foundry
- `SKCompletionService` — Semantic Kernel

**DocumentGen** — Generate code-style and wiki docs from golden repos:
- `SKDocumentGenerationService` (default) — Semantic Kernel
- `FoundryDocumentGenerationService` — Azure AI Foundry

**Github** — Repository indexing pipeline:
- `RepositoryIndexer` — Orchestrates fetching, chunking, and embedding
- `ContentProcessor` — Parses and chunks source files
- `GitHubHTTPClient` — Async HTTP client for the GitHub API

**Storage:**
- `WorkspaceContentStore` — SQLite-backed vector store persisted to `.archAngel/knowledge.ca`
- `ContentStoreFactory` / `ContentStoreProvider` — Per-workspace store lifecycle

**Utils:**
- `GoldenRepoSearchService` — Semantic search across indexed golden repos
- `CitationProcessor` — Extracts and formats citations in AI responses
- `ProjectContextBuilder` — Gathers workspace context for prompts
- `WorkspaceFileService` — File system access for the current workspace

**Shared Models** (`ArchAngel.Contracts/Models.cs`):
- DTOs for chat, analysis, indexing requests/responses

## Project Structure

```
archangel/
├── src/                                    # VS Code Extension (TypeScript)
│   ├── extension.ts                       # Activation, commands, view providers
│   ├── AgentService.ts                    # LSP client (launches .NET backend)
│   ├── ChatViewProvider.ts                # Chat webview provider
│   ├── SettingsViewProvider.ts            # Knowledge base settings webview
│   ├── chat.js                            # Chat panel frontend
│   ├── settings.js                        # Settings panel frontend
│   └── test/                              # Extension tests
│
├── ArchAngel.Service/                      # Backend Service (.NET 9)
│   └── ArchAngel.Service/
│       ├── Program.cs                     # DI setup, Azure credential config
│       ├── ArchAngelLanguageServer.cs     # LSP server (StreamJsonRpc)
│       ├── PromptConfig.cs                # RAG prompt templates
│       ├── Chat/                          # Chat services (Foundry, SK, SKMemory)
│       ├── Completion/                    # Code completion services
│       ├── DocumentGen/                   # Doc generation (code style, wiki)
│       ├── Github/                        # Repo indexing & content processing
│       ├── Storage/                       # SQLite-backed vector store
│       ├── Utils/                         # Search, citations, context building
│       ├── Configuration/                 # ConfigurationManager
│       └── appsettings.json
│
├── ArchAngel.Contracts/                    # Shared DTOs (Models.cs)
├── media/                                  # Extension icon (archangel-icon.svg)
├── package.json                            # Extension manifest & settings schema
├── archangel.json                          # Demo golden-repo config
├── archangel.sln                           # .NET solution file
├── .env.example                            # Environment variable template
├── CHANGELOG.md                            # Version history
└── README.md
```

## Getting Started

### Prerequisites

- **Node.js** 18+ (for the VS Code extension)
- **.NET SDK** 9.0+ (for the backend service)
- **Visual Studio Code** 1.104.0+
- **Azure AI Foundry** project with:
  - A chat deployment (default: `gpt-4o`)
  - A completions deployment (default: `gpt-4o-mini`)
  - An embeddings deployment (default: `text-embedding-3-large`)
- **Azure identity** configured (`DefaultAzureCredential` — e.g. `az login`)
- **GitHub Token** (optional, for indexing private repositories)

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://github.com/rohitmadhavk/ArchAngel.git
   cd archangel
   ```

2. **Install Node.js dependencies:**
   ```bash
   npm install
   ```

3. **Restore .NET dependencies:**
   ```bash
   cd ArchAngel.Service/ArchAngel.Service
   dotnet restore
   cd ../..
   ```

4. **Set up environment variables or setup fallback config in Program.cs:**
   ```bash
   cp .env.example .env
   ```
   Edit `.env` with your endpoints (see [Configuration](#configuration) for the full list):
   ```env
   AZURE_FOUNDRY_ENDPOINT=https://<your-resource>.services.ai.azure.com/api/projects/<project>
   AZURE_OPENAI_ENDPOINT=https://<your-resource>.openai.azure.com/openai/v1
   ```

### Running the Extension

**Development Mode:**

1. **Build TypeScript** (or use watch mode):
   ```bash
   npm run compile
   npm run watch      # rebuilds on change
   ```

2. **Launch the extension in VS Code:**
   - Press `F5` (or use **Run > Start Debugging**).
   - The extension will automatically start the .NET backend via LSP.
   - A new VS Code window opens with ArchAngel installed.

> **Note:** You do not need to start the backend manually — the extension spawns it as a child process via `AgentService.ts`.

**Production Build:**

```bash
npm run vscode:prepublish
```

## Configuration

### Environment Variables

The backend reads these variables at startup (all have sensible defaults except the two endpoints):

| Variable | Description | Default |
|---|---|---|
| `AZURE_FOUNDRY_ENDPOINT` | Azure AI Foundry project endpoint | *(required)* |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint | *(required)* |
| `AI_MODEL_DEPLOYMENT_CHAT` | Chat model deployment name | `gpt-4o` |
| `AI_MODEL_DEPLOYMENT_COMPLETIONS` | Code completion model deployment | `gpt-4o-mini` |
| `AI_MODEL_DEPLOYMENT_DOCUMENTGEN` | Document generation model deployment | `gpt-4o` |
| `AI_MODEL_DEPLOYMENT_EMBEDDINGS` | Embedding model deployment | `text-embedding-3-large` |

Authentication uses `DefaultAzureCredential` (no API keys required).

### Extension Settings

Configurable via VS Code settings UI or `.vscode/settings.json`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `archAngel.knowledgeBase.repositories` | array | `[]` | GitHub repositories to index as golden repos |
| `archAngel.github.token` | string | `""` | GitHub PAT for private repositories |
| `archAngel.knowledgeBase.maxFileSize` | number | `100000` | Max file size (bytes) to index |
| `archAngel.knowledgeBase.chunkSize` | number | `1000` | Max chunk size for indexed content |
| `archAngel.knowledgeBase.autoIndex` | boolean | `false` | Auto-index enabled repos on startup |
| `archAngel.knowledgeBase.includedFileTypes` | array | 26 extensions | File types to include when indexing |
| `ArchAngel.Service.endpoint` | string | `""` | Custom backend endpoint (leave empty for default) |
| `archAngel.logging.level` | enum | `"Info"` | Logging level (`Error`, `Warning`, `Info`, `Debug`) |

## Development

### Architecture Patterns

#### Dependency Injection
Services are registered in `Program.cs` using the built-in DI container:
```csharp
services.AddSingleton<IChatService, FoundryChatService>();
services.AddSingleton<ICompletionService, FoundryCompletionService>();
services.AddSingleton<IDocumentGenerationService, SKDocumentGenerationService>();
services.AddSingleton<IContentStoreProvider, ContentStoreProvider>();
// RepositoryIndexer created via factory (requires workspace-scoped IContentStore)
services.AddSingleton<Func<IContentStore, IRepositoryIndexer>>(sp => ...);
```

#### Language Server Protocol (LSP)
- Extension (`AgentService.ts`) spawns the backend and connects over **stdio**
- Backend (`ArchAngelLanguageServer.cs`) uses **StreamJsonRpc** to handle LSP messages
- Custom request types for chat, indexing, completion, and document generation

#### RAG Pipeline
1. User sends a message in the chat panel
2. `GoldenRepoSearchService` runs a semantic similarity search against indexed golden repos
3. Top matching chunks + citations are injected into the system prompt via `PromptConfig`
4. The LLM generates a response with numbered source citations

### Building & Testing

```bash
# TypeScript
npm run compile
npm run lint
npm test

# .NET (from repo root)
dotnet build
dotnet test
```

### Debugging

- Press `F5` in VS Code to launch the extension in a new window.
- The .NET backend is started automatically; attach a debugger to the `dotnet` process if needed.
- Set `archAngel.logging.level` to `Debug` for verbose backend output.

## Commands

All commands are available from the Command Palette (`Ctrl+Shift+P`):

| Command | Description |
|---|---|
| **ArchAngel: Generate a Code Style Document** | Generate a code style guide from your golden repos |
| **ArchAngel: Generate a Wiki Document** | Generate wiki documentation from your golden repos |
| **ArchAngel: Index Repositories from Config** | Index repos defined in `archangel.json` |
| **ArchAngel: Wipe Knowledge Base** | Clear all indexed data |
| **Add Repository to Knowledge Base** | Add a new GitHub repo to the knowledge base |
| **Refresh Knowledge Base** | Re-index all enabled repositories |

## Known Issues

| Issue | Detail |
|---|---|
| **Cold start** | First request may be slow while `DefaultAzureCredential` resolves and the embedding service warms up |
| **Large repositories** | Indexing repos with 10k+ files is memory-intensive; prefer focused repos or filter by file type |
| **Token limits** | RAG context is truncated to stay within model token limits |
| **Embedding costs** | Each indexed repo generates embeddings that consume Azure OpenAI quota |

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

**Guidelines:**
- Follow standard C# and TypeScript conventions
- Add tests for new features
- Update this README for user-facing changes

## Acknowledgments

Built with:
- [VS Code Extension API](https://code.visualstudio.com/api)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel) 1.65.0
- [Azure AI Foundry SDK](https://learn.microsoft.com/azure/ai-studio/)
- [Azure OpenAI](https://azure.microsoft.com/en-us/products/ai-services/openai-service/)
- [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc) (LSP transport)
- [.NET 9](https://dotnet.microsoft.com/)