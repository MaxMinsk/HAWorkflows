import eslint from "@eslint/js";
import tseslint from "typescript-eslint";
import importPlugin from "eslint-plugin-import-x";

export default tseslint.config(
  eslint.configs.recommended,
  ...tseslint.configs.recommended,
  {
    plugins: {
      "import-x": importPlugin
    },
    rules: {
      "import-x/no-restricted-paths": [
        "error",
        {
          zones: [
            {
              target: "./src/shared/**",
              from: "./src/features/**",
              message: "shared/ must not import from features/"
            },
            {
              target: "./src/shared/**",
              from: "./src/app/**",
              message: "shared/ must not import from app/"
            },
            {
              target: "./src/features/workflow/**",
              from: "./src/features/editor/**",
              message: "workflow feature must not import from editor feature directly; compose at app level"
            },
            {
              target: "./src/features/workflow/**",
              from: "./src/features/runs/**",
              message: "workflow feature must not import from runs feature directly; compose at app level"
            },
            {
              target: "./src/features/workflow/**",
              from: "./src/features/settings/**",
              message: "workflow feature must not import from settings feature directly; compose at app level"
            },
            {
              target: "./src/features/editor/**",
              from: "./src/features/workflow/**",
              message: "editor feature must not import from workflow feature directly"
            },
            {
              target: "./src/features/runs/**",
              from: "./src/features/workflow/**",
              message: "runs feature must not import from workflow feature directly"
            },
            {
              target: "./src/features/settings/**",
              from: "./src/features/workflow/**",
              message: "settings feature must not import from workflow feature directly"
            }
          ]
        }
      ],
      "@typescript-eslint/no-unused-vars": [
        "warn",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }
      ],
      "@typescript-eslint/no-explicit-any": "warn"
    }
  },
  {
    ignores: ["dist/**", "../wwwroot/**", "node_modules/**", "*.config.*"]
  }
);
