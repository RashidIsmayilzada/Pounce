import AppKit
import PounceKit
import SwiftUI

/// The search panel: a big search field, then the Results with their Snippets.
struct LauncherView: View {
    @Bindable var model: SearchModel
    var onOpen: (ResultItem) -> Void = { _ in }
    @FocusState private var fieldFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 20, weight: .medium))
                    .foregroundStyle(.secondary)
                TextField("Search your files", text: $model.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 22))
                    .focused($fieldFocused)
                if !model.query.isEmpty && !model.isFinal {
                    ProgressView().controlSize(.small)
                }
            }
            .padding(.horizontal, 18)
            .frame(height: 56)

            if let message = model.message {
                Divider()
                Text(message)
                    .font(.system(size: 13))
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(18)
            } else if !model.results.isEmpty {
                Divider()
                VStack(spacing: 2) {
                    ForEach(Array(model.results.enumerated()), id: \.element.id) { index, item in
                        ResultRow(item: item, selected: index == model.selection)
                            .contentShape(Rectangle())
                            .onTapGesture { model.selection = index; onOpen(item) }
                    }
                }
                .padding(6)
            }
        }
        .frame(width: 680)
        .background(.regularMaterial)
        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        .onAppear { fieldFocused = true }
    }
}

struct ResultRow: View {
    let item: ResultItem
    let selected: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: item.path))
                .resizable()
                .frame(width: 32, height: 32)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(item.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                    Text(abbreviated(item.path)).font(.system(size: 11)).foregroundStyle(.secondary)
                        .lineLimit(1).truncationMode(.middle)
                }
                if !item.snippet.text.isEmpty {
                    snippet.font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(2)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(selected ? Color.accentColor.opacity(0.22) : .clear,
                    in: RoundedRectangle(cornerRadius: 8, style: .continuous))
    }

    /// The Snippet with matched words in bold and full-strength colour.
    private var snippet: Text {
        item.snippet.runs.reduce(Text("")) { text, run in
            let clean = run.text.replacingOccurrences(of: "\n", with: " ")
            return text + (run.highlighted ? Text(clean).bold().foregroundColor(.primary) : Text(clean))
        }
    }

    private func abbreviated(_ path: String) -> String {
        (path as NSString).abbreviatingWithTildeInPath
    }
}
