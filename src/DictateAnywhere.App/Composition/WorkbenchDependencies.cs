using System;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Composition;

internal sealed record WorkbenchDependencies(
  IChatFileDialogService ChatFileDialogService,
  IChatExportFileDialogService ChatExportFileDialogService,
  LocalChatProviderRegistry ChatProviderRegistry,
  Func<ReaderWindow> ReaderWindowFactory,
  WorkbenchReadAloudController ReadAloudController,
  WorkbenchOperationSession OperationSession,
  WorkbenchDictationController DictationController,
  WorkbenchDictationCommandController DictationCommandController,
  WorkbenchChatController ChatController,
  WorkbenchChatSendController ChatSendController,
  WorkbenchChatModelSetupCommandController ChatModelSetupCommandController,
  WorkbenchFileImportCommandController FileImportCommandController,
  WorkbenchQuickSettingsController QuickSettingsController,
  WorkbenchSettingsApplicationController SettingsApplicationController,
  WorkbenchHistoryController HistoryController,
  WorkbenchHistoryInteractionController HistoryInteractionController);
