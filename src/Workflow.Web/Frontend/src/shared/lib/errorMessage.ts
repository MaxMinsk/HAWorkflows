/**
 * Что: безопасная нормализация ошибки в человекочитаемую строку.
 * Зачем: единообразно формировать сообщения об ошибках для UI.
 * Как: извлекает `message` из unknown error, иначе возвращает fallback.
 */
export function getErrorMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object" && "message" in error && typeof error.message === "string") {
    return error.message;
  }

  return fallback;
}
