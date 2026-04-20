declare module '_framework_dotnet' {
  export interface DotnetModule {
    withDiagnosticTracing(enabled: boolean): this;
    create(): Promise<{
      getAssemblyExports(assemblyName: string): Promise<unknown>;
      dispose(): void;
    }>;
  }
  export const dotnet: DotnetModule;
}
