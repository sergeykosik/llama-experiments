import express from "express";
import bodyParser from "body-parser";
import { initializeLlama, processMessage } from "./llama.service.js";

const app = express();
const port = 3000;

// Middleware to parse JSON bodies
app.use(bodyParser.json());

// Initialize Llama model and session
initializeLlama()
  .then(() => {
    console.log("✔️  Llama model initialization complete");
  })
  .catch((error) => {
    console.error("Error initializing Llama model:", error);
  });

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
    console.error("❌ Error processing message with Llama:", error);
    res.status(500).json({ error: "Failed to process message" });
  }
});

// Start the server
app.listen(port, () => {
  console.log(`🚀 Server is running on http://localhost:${port}`);
});
