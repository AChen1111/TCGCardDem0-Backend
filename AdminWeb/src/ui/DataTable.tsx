export type Column<T> = {
  key: keyof T & string;
  title: string;
  kind?: "text" | "number" | "bool";
  width?: string;
};

type Props<T extends object> = {
  rows: T[];
  columns: Column<T>[];
  readOnly?: boolean;
  onChange?: (index: number, key: keyof T, value: string | number | boolean) => void;
  onRemove?: (index: number) => void;
};

export function DataTable<T extends object>({
  rows,
  columns,
  readOnly,
  onChange,
  onRemove
}: Props<T>) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key} style={column.width ? { width: column.width } : undefined}>
                {column.title}
              </th>
            ))}
            {!readOnly && onRemove ? <th className="col-action"> </th> : null}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length + (!readOnly && onRemove ? 1 : 0)} className="empty">
                没有数据
              </td>
            </tr>
          ) : (
            rows.map((row, index) => (
              <tr key={index}>
                {columns.map((column) => {
                  const value = row[column.key];
                  if (readOnly) {
                    return (
                      <td key={column.key}>
                        {column.kind === "bool" ? (value ? "是" : "否") : String(value ?? "")}
                      </td>
                    );
                  }

                  if (column.kind === "bool") {
                    return (
                      <td key={column.key}>
                        <input
                          type="checkbox"
                          checked={Boolean(value)}
                          onChange={(event) => onChange?.(index, column.key, event.target.checked)}
                        />
                      </td>
                    );
                  }

                  return (
                    <td key={column.key}>
                      <input
                        type={column.kind === "number" ? "number" : "text"}
                        value={value == null ? "" : String(value)}
                        onChange={(event) => {
                          const next =
                            column.kind === "number"
                              ? event.target.value === ""
                                ? 0
                                : Number(event.target.value)
                              : event.target.value;
                          onChange?.(index, column.key, next);
                        }}
                      />
                    </td>
                  );
                })}
                {!readOnly && onRemove ? (
                  <td className="col-action">
                    <button className="link" type="button" onClick={() => onRemove(index)}>
                      删
                    </button>
                  </td>
                ) : null}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
