const shortDateFormatter = new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" });
const shortTimeFormatter = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" });
const tokenFormatter = new Intl.NumberFormat();

export function formatDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : shortDateFormatter.format(date);
}

export function formatTime(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : shortTimeFormatter.format(date);
}

export function displayTokens(tokens) {
  return tokens == null ? "not reported" : tokenFormatter.format(tokens);
}
