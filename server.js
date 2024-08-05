import express from "express";
import bodyParser from "body-parser";
import { initializeLlama, processMessage } from "./llama.service.js";

const app = express();
const port = 3000;

// Middleware to parse JSON bodies
app.use(bodyParser.json());

// Initialize Llama model and session
const initializeModel = async () => {
    try {
      await initializeLlama();
      console.log("✔️  Llama model initialization complete");
    } catch (error) {
      console.error("Error initializing Llama model:", error);
    }
  };

// Initial initialization
initializeModel();

// Endpoint to handle POST requests
app.post("/process-message", async (req, res) => {
  const message = req.body.message;

  if (!message) {
    return res.status(400).json({ error: "⛔ Message is required" });
  }

  try {
    const response = await processMessage(message);
    res.json(response);
  } catch (error) {
    // Check for KV slot error and return specific status code
    // so the client can post "restart" command to restart the server
    if (error.message.includes("could not find a KV slot for the batch")) {
        console.error("❌ KV slot error:", error);
        return res.status(503).json({
          error: "KV slot error. Please restart the server.",
          details: error.message,
        });
      }

    console.error("❌ Error processing message with Llama:", error);
    res.status(500).json({ error: "Failed to process message" });
  }
});

// Endpoint to restart the Llama model
app.post("/restart", async (req, res) => {
    try {
      await initializeModel();
      res.json({ message: "✔️  Llama model re-initialization complete" });
    } catch (error) {
      console.error("❌ Error re-initializing Llama model:", error);
      res.status(500).json({ error: "Failed to re-initialize Llama model" });
    }
  });

// Start the server
app.listen(port, () => {
  console.log(`🚀 Server is running on http://localhost:${port}`);
});
