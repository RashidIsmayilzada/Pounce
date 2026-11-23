import AppKit
import PounceKit
import SwiftUI

/// The search panel: a big search field, then matching applications, then the Results with their Snippets.
struct LauncherView: View {
    @Bindable var model: SearchModel
    var onOpen: (SearchModel.Item) -> Void = { _ in }
    var onHeightChange: (CGFloat) -> Void = { _ in }
    /// For offscreen rendering (snapshots, the demo video): draw this text and a cursor instead of the live field,
    /// which the offscreen renderer cannot draw.
    var renderedQuery: String?
    var displayPath: (String) -> String = { ($0 as NSString).abbreviatingWithTildeInPath }
    @FocusState private var fieldFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 20, weight: .medium))
                    .foregroundStyle(.secondary)
                if let renderedQuery {
                    (renderedQuery.isEmpty
                        ? Text("Search apps and files").foregroundColor(.secondary)
                        : Text(renderedQuery) + Text("|").foregroundColor(.accentColor))
                        .font(.system(size: 22))
                        .frame(maxWidth: .infinity, alignment: .leading)
                } else {
                    TextField("Search apps and files", text: $model.query)
                        .textFieldStyle(.plain)
                        .font(.system(size: 22))
                        .focused($fieldFocused)
                }
                if !model.query.isEmpty && !model.isFinal {
                    ProgressView().controlSize(.small)
                }
            }
            .padding(.horizontal, 18)
            .frame(height: 56)

            if !model.apps.isEmpty || !model.results.isEmpty || model.message != nil {
                Divider()
                VStack(alignment: .leading, spacing: 2) {
                    if !model.apps.isEmpty {
                        sectionTitle("Applications")
                        ForEach(Array(model.apps.enumerated()), id: \.element.id) { index, app in
                            AppRow(app: app, selected: index == model.selection)
                                .contentShape(Rectangle())
                                .onTapGesture { model.selection = index; onOpen(.app(app)) }
                        }
                    }
                    if let message = model.message {
                        Text(message)
                            .font(.system(size: 13))
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(12)
                    } else if !model.results.isEmpty {
                        if !model.apps.isEmpty { sectionTitle("Files") }
                        ForEach(Array(model.results.enumerated()), id: \.element.id) { index, item in
                            let row = model.apps.count + index
                            ResultRow(item: item, selected: row == model.selection, displayPath: displayPath)
                                .contentShape(Rectangle())
                                .onTapGesture { model.selection = row; onOpen(.file(item)) }
                        }
                    }
                }
                .padding(6)
            }
        }
        .frame(width: 680)
        .background(renderedQuery == nil ? AnyShapeStyle(.regularMaterial) : AnyShapeStyle(Color(white: 0.97)))
        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        .background(GeometryReader { proxy in
            Color.clear
                .onAppear { onHeightChange(proxy.size.height) }
                .onChange(of: proxy.size.height) { _, height in onHeightChange(height) }
        })
        .onAppear { fieldFocused = true }
        .onReceive(NotificationCenter.default.publisher(for: LauncherPanel.didShow)) { _ in fieldFocused = true }
    }
}

private func sectionTitle(_ title: String) -> some View {
    Text(title)
        .font(.system(size: 11, weight: .semibold))
        .foregroundStyle(.secondary)
        .padding(.horizontal, 12)
        .padding(.top, 6)
}

/// An application: its icon and name, like Spotlight's top hits.
struct AppRow: View {
    let app: AppEntry
    let selected: Bool

    var body: some View {
        HStack(spacing: 12) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: app.path))
                .resizable()
                .frame(width: 32, height: 32)
            Text(app.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
            Spacer(minLength: 0)
            if selected {
                Text("Open").font(.system(size: 11)).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(selected ? Color.accentColor.opacity(0.22) : .clear,
                    in: RoundedRectangle(cornerRadius: 8, style: .continuous))
    }
}

struct ResultRow: View {
    let item: ResultItem
    let selected: Bool
    var displayPath: (String) -> String = { ($0 as NSString).abbreviatingWithTildeInPath }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: item.path))
                .resizable()
                .frame(width: 32, height: 32)
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(item.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                    Text(displayPath(item.path)).font(.system(size: 11)).foregroundStyle(.secondary)
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
            // PDFs pad columns with runs of spaces; show them as one.
            let clean = run.text.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            return text + (run.highlighted ? Text(clean).bold().foregroundColor(.primary) : Text(clean))
        }
    }

}
