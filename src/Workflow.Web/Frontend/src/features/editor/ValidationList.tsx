import React from "react";

/**
 * Что: список ошибок валидации графа.
 * Зачем: единая визуализация validation output.
 * Как: при пустом списке показывает положительный статус.
 */
interface ValidationListProps {
  errors: string[];
}

export function ValidationList({ errors }: ValidationListProps) {
  const hasErrors = Array.isArray(errors) && errors.length > 0;
  if (!hasErrors) {
    return (
      <ul className="validation-list">
        <li className="validation-item ok">No validation issues</li>
      </ul>
    );
  }

  return (
    <ul className="validation-list">
      {errors.map((error, index) => (
        <li className="validation-item error" key={`${index}:${error}`}>
          {error}
        </li>
      ))}
    </ul>
  );
}
